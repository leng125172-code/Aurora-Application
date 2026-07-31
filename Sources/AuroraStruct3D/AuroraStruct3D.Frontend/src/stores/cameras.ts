/**
 * 相机 Pinia Store
 *
 * 负责：
 *  1. 维护相机设备列表响应式数据
 *  2. 通过 SignalR Hub（/signalr-hubs/camera，MessagePack 协议）
 *     接收实时状态推送、运行指标、GenICam 节点变更通知
 *  3. 相机帧预览：使用 HTTP MJPEG 流（Multipart/x-mixed-replace），
 *     路由 /api/streaming/cameras/{id}/preview，浏览器原生 <img> 播放
 *  4. 封装所有手动控制操作
 */
import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import {
    type CameraDeviceDto,
    type CameraDeviceInfoDto,
    type CameraLiveMetricsDto,
    type CameraRtpEndpointDto,
    type CameraSnapshotDto,
    type CameraScanResultDto,
    type StartCameraPreviewDto,
    type GetCameraListDto,
    type UpdateCameraDeviceDto,
    getCameraList,
    getCamera,
    updateCamera,
    openCamera,
    closeCamera,
    scanCameras,
    getCameraDeviceInfo as getDeviceInfo,
    takeSnapshot,
    startPreview,
    stopPreview,
    doSoftwareTrigger,
    doExposureAutoOncePulse,
    getRtpEndpoint,
    getCameraImageRotationAngle,
    setCameraImageRotationAngle,
    type GenICamNodeGetInput,
    type GenICamBatchGetResultDto,
    type GenICamNodeSetInput,
    type CameraNodeMapDto,
    type CameraStateDto,
    type GenICamNodeChangeDto,
    type CameraSnapshotStateDto,
    batchGetGenICamParams as apiBatchGetGenICamParams,
    setGenICamParam as apiSetGenICamParam,
    executeGenICamCommand as apiExecuteGenICamCommand,
    getCameraNodeMap as apiGetCameraNodeMap,
    refreshCameraNodeMap as apiRefreshCameraNodeMap,
    readCameraNodes as apiReadCameraNodes,
    getCameraSnapshotState as apiGetCameraSnapshotState,
} from '@/api/cameras'

export const useCameraStore = defineStore('camera', () => {
    // ─── 状态 ─────────────────────────────────────────────────────────────────

    /** 相机设备列表 */
    const cameras = ref<CameraDeviceDto[]>([])
    /** 当前选中的相机（控制页使用） */
    const selectedCamera = ref<CameraDeviceDto | null>(null)
    /** 是否正在加载列表 */
    const loading = ref(false)
    /** SignalR 连接状态 */
    const hubConnected = ref(false)
    /**
     * 实时预览 URL，key 为相机 ID。
     * 值为 Blob URL（`blob:` 开头）：由 fetch ReadableStream 自行解析 multipart/BMP
     * 后 URL.createObjectURL 产生，赋给 <img :src> 由浏览器原生解码 BMP。
     * （Chrome 的原生 MJPEG multipart 解析器只支持 JPEG，因此必须自行分帧。）
     */
    const previewFrames = ref<Map<string, string>>(new Map())
    /** 预览流控制器，key 为相机 ID；停止预览或切换时 abort() 断开底层 fetch 连接 */
    const _streamAbortCtrls = new Map<string, AbortController>()
    /** 实时运行指标，key 为相机 ID */
    const liveMetrics = ref<Map<string, CameraLiveMetricsDto>>(new Map())
    /** 实时相机状态（CameraStateDto），key 为相机 ID */
    const cameraStates = ref<Map<string, CameraStateDto>>(new Map())
    /** 动态 NodeMap 缓存，key 为相机 ID */
    const nodeMaps = ref<Map<string, CameraNodeMapDto>>(new Map())
    /** 已订阅快照状态恢复的相机 ID 集合（sessionStorage 持久化，重连时用于批量恢复） */
    const subscribedCameraIds = ref<Set<string>>(new Set())

    /** sessionStorage key */
    const SESSION_KEY = 'camera:subscribedIds'

    /** 从 sessionStorage 恢复订阅集合 */
    function _loadSubscribedIds() {
        try {
            const raw = sessionStorage.getItem(SESSION_KEY)
            if (raw) {
                const ids: string[] = JSON.parse(raw)
                subscribedCameraIds.value = new Set(ids)
            }
        } catch {
            // 忽略解析失败
        }
    }

    /** 持久化订阅集合到 sessionStorage */
    function _saveSubscribedIds() {
        try {
            sessionStorage.setItem(SESSION_KEY, JSON.stringify([...subscribedCameraIds.value]))
        } catch {
            // 忽略写入失败
        }
    }

    _loadSubscribedIds()

    let connection: signalR.HubConnection | null = null

    // ─── 辅助函数 ─────────────────────────────────────────────────────────────

    type LiveMetricsWire = Partial<CameraLiveMetricsDto> & Record<string, unknown>

    function toNumber(value: unknown, fallback = 0): number {
        if (typeof value === 'number' && Number.isFinite(value)) return value
        if (typeof value === 'string') {
            const parsed = Number(value)
            if (Number.isFinite(parsed)) return parsed
        }
        return fallback
    }

    /** SignalR MessagePack 会保留 C# PascalCase 属性名，这里统一转成前端 camelCase。 */
    function normalizeLiveMetrics(raw: LiveMetricsWire): CameraLiveMetricsDto {
        return {
            fpgaTemperature: toNumber(raw.fpgaTemperature ?? raw.FpgaTemperature),
            sensorTemperature: toNumber(raw.sensorTemperature ?? raw.SensorTemperature),
            frameRate: toNumber(raw.frameRate ?? raw.FrameRate),
            aeStatus: toNumber(raw.aeStatus ?? raw.AeStatus),
            currentBufFrames: toNumber(raw.currentBufFrames ?? raw.CurrentBufFrames),
            focusScore: toNumber(raw.focusScore ?? raw.FocusScore),
            apertureScore: toNumber(raw.apertureScore ?? raw.ApertureScore),
            apertureHint: toNumber(raw.apertureHint ?? raw.ApertureHint),
        }
    }

    // ─── HTTP MJPEG 流：multipart 解析工具 ──────────────────────────────────

    /** Part 分隔符（短横前缀 + 后端常量值），与 CameraStreamingEndpoints.cs 保持一致 */
    const MJPEG_BOUNDARY = '--auroraframe7f3d9a2b'
    const MJPEG_SEP = MJPEG_BOUNDARY + '\r\n'
    const MJPEG_HEADERS_END = '\r\n\r\n'

    /**
     * 文本编码工具：用于在 Uint8Array 缓冲区中查找 ASCII 分隔符序列（Content-Length、boundary 等）
     */
    const _enc = new TextEncoder()
    const _dec = new TextDecoder('ascii')
    const SEP_BYTES = _enc.encode(MJPEG_SEP)
    const HEADERS_END_BYTES = _enc.encode(MJPEG_HEADERS_END)

    /** 在 haystack 缓冲区 [start,end) 内查找 pattern 字节序列，返回起始位置；找不到返回 -1 */
    function _indexOfBytes(haystack: Uint8Array, pattern: Uint8Array, start = 0, end = haystack.length): number {
        const hEnd = Math.min(end, haystack.length)
        if (pattern.length === 0 || hEnd - start < pattern.length) return -1
        outer: for (let i = start; i <= hEnd - pattern.length; i++) {
            for (let j = 0; j < pattern.length; j++) {
                if (haystack[i + j] !== pattern[j]) continue outer
            }
            return i
        }
        return -1
    }

    /**
     * 启动某台相机的 HTTP MJPEG 流读取 + multipart 解析协程：
     *  1. fetch('/api/streaming/cameras/{id}/preview') 获取 ReadableStream
     *  2. 按 MJPEG_SEP 切分出每个 Part
     *  3. 在 Part 头部解析 Content-Length → 读取指定长度 BMP 字节 → Blob + ObjectURL → 写入 previewFrames
     *  4. 下一帧写入前自动 revoke 上一帧 Blob URL，防止内存泄漏
     */
    async function _startStreamReader(id: string): Promise<void> {
        // 若已有老连接，先关闭再开新连接
        const oldCtrl = _streamAbortCtrls.get(id)
        if (oldCtrl) {
            try { oldCtrl.abort() } catch { /* ignore */ }
            _streamAbortCtrls.delete(id)
            const prevUrl = previewFrames.value.get(id)
            if (prevUrl) {
                URL.revokeObjectURL(prevUrl)
                previewFrames.value.delete(id)
            }
        }

        const ctrl = new AbortController()
        _streamAbortCtrls.set(id, ctrl)

        try {
            const resp = await fetch(`/api/streaming/cameras/${id}/preview?t=${Date.now()}`, {
                signal: ctrl.signal,
                cache: 'no-store',
            })
            if (!resp.ok || !resp.body) {
                console.warn(
                    `[cameras] 相机 ${id} MJPEG 流连接失败 status=${resp.status}`
                )
                return
            }

            // 从响应头或默认值获取帧 MIME
            const frameContentType = resp.headers.get('X-Frame-Content-Type') ?? 'image/bmp'

            const reader = resp.body.getReader()
            let buffer = new Uint8Array(0)
            let lastBlobUrl: string | null = null

            try {
                while (true) {
                    // 1. 读取下一块并追加到 buffer
                    const { done, value } = await reader.read()
                    if (done) break
                    if (value && value.length > 0) {
                        const merged = new Uint8Array(buffer.length + value.length)
                        merged.set(buffer, 0)
                        merged.set(value, buffer.length)
                        buffer = merged
                    }

                    // 2. 尝试解析出所有完整 Part
                    //    一个完整 Part 在 buffer 中应该形如：
                    //    {MJPEG_SEP} Content-Length: NNN \r\n ... \r\n\r\n <NNN bytes of BMP>
                    while (true) {
                        const sepIdx = _indexOfBytes(buffer, SEP_BYTES)
                        if (sepIdx === -1) break // 还没凑齐分隔符

                        const headersStart = sepIdx + SEP_BYTES.length

                        // 寻找 Part headers 的结束标记
                        const headersEnd = _indexOfBytes(buffer, HEADERS_END_BYTES, headersStart)
                        if (headersEnd === -1) break // headers 还没凑齐，等下一个 read

                        // 解析 headers（ASCII），主要是 Content-Length
                        let contentLen = -1
                        const headersText = _dec.decode(buffer.subarray(headersStart, headersEnd))
                        for (const line of headersText.split(/\r?\n/)) {
                            const t = line.trim()
                            if (t.length === 0) continue
                            const colon = t.indexOf(':')
                            if (colon <= 0) continue
                            const key = t.slice(0, colon).trim().toLowerCase()
                            const val = t.slice(colon + 1).trim()
                            if (key === 'content-length') {
                                contentLen = parseInt(val, 10)
                                if (isNaN(contentLen)) contentLen = -1
                            }
                        }

                        if (contentLen <= 0) {
                            // 非法 part：没有 Content-Length，丢弃该分隔符后重试
                            console.warn('[cameras] MJPEG part 缺少合法 Content-Length，跳过')
                            buffer = buffer.subarray(headersStart)
                            continue
                        }

                        const payloadStart = headersEnd + HEADERS_END_BYTES.length
                        const needTotal = payloadStart + contentLen
                        if (buffer.length < needTotal) {
                            // 帧体尚未收齐，等下一个 read
                            break
                        }

                        // 3. 完整帧到手 → 组装 Blob → 赋给 store
                        const frameBytes = buffer.subarray(payloadStart, payloadStart + contentLen)
                        const blob = new Blob([frameBytes], { type: frameContentType })
                        const url = URL.createObjectURL(blob)

                        previewFrames.value.set(id, url)

                        // 释放上一帧（不干扰已 set 的新 url）
                        if (lastBlobUrl) URL.revokeObjectURL(lastBlobUrl)
                        lastBlobUrl = url

                        // buffer 消费到 payloadStart + contentLen 之后，继续 while 解下一帧
                        buffer = buffer.subarray(payloadStart + contentLen)
                    }
                }
            } finally {
                try { reader.releaseLock() } catch { /* ignore */ }
                if (lastBlobUrl) URL.revokeObjectURL(lastBlobUrl)
            }
        } catch (err: unknown) {
            if (
                (err instanceof Error && err.name === 'AbortError') ||
                (typeof (err as { name?: string })?.name === 'string' && (err as { name: string }).name === 'AbortError')
            ) {
                // 正常停止，静默
            } else {
                console.warn(`[cameras] 相机 ${id} MJPEG 流读取异常：`, err)
            }
        } finally {
            if (_streamAbortCtrls.get(id) === ctrl) _streamAbortCtrls.delete(id)
            // 断开连接后清除预览帧
            const cur = previewFrames.value.get(id)
            if (cur) {
                URL.revokeObjectURL(cur)
                previewFrames.value.delete(id)
            }
        }
    }

    /** 立即停止某台相机的 HTTP MJPEG 流读取（abort fetch），并释放 Blob URL */
    function _stopStreamReader(id: string) {
        const ctrl = _streamAbortCtrls.get(id)
        if (ctrl) {
            try { ctrl.abort() } catch { /* ignore */ }
            _streamAbortCtrls.delete(id)
        }
        const cur = previewFrames.value.get(id)
        if (cur) {
            URL.revokeObjectURL(cur)
            previewFrames.value.delete(id)
        }
    }

    /** 用最新数据更新列表中某台相机 */
    function _applyUpdate(updated: CameraDeviceDto) {
        const idx = cameras.value.findIndex((c) => c.id === updated.id)
        if (idx >= 0) {
            cameras.value[idx] = updated
        } else {
            cameras.value.push(updated)
        }
        if (selectedCamera.value?.id === updated.id) {
            selectedCamera.value = updated
        }
    }

    // ─── SignalR ──────────────────────────────────────────────────────────────

    /** 心跳定时器 ID */
    let _heartbeatTimer: ReturnType<typeof setInterval> | null = null

    /** 启动心跳（每 15 秒 ping 一次，维持连接活跃） */
    function _startHeartbeat() {
        _stopHeartbeat()
        _heartbeatTimer = setInterval(async () => {
            if (connection?.state === signalR.HubConnectionState.Connected) {
                try {
                    await connection.invoke<number>('PingAsync')
                } catch {
                    // 心跳失败时 SignalR 自动重连机制会处理，此处忽略
                }
            }
        }, 15_000)
    }

    /** 停止心跳定时器 */
    function _stopHeartbeat() {
        if (_heartbeatTimer !== null) {
            clearInterval(_heartbeatTimer)
            _heartbeatTimer = null
        }
    }

    /** 启动 SignalR Hub 连接（使用 MessagePack 协议，用于状态/指标推送，不再按帧推送） */
    async function startHub() {
        if (connection?.state === signalR.HubConnectionState.Connected) {
            hubConnected.value = true
            return
        }

        if (connection && connection.state !== signalR.HubConnectionState.Disconnected) {
            throw new Error(`相机实时连接尚未就绪：${connection.state}`)
        }

        if (!connection) {
            connection = new signalR.HubConnectionBuilder()
                .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
                .withHubProtocol(new MessagePackHubProtocol())
                .withAutomaticReconnect([0, 2000, 5000, 10000])
                .configureLogging(signalR.LogLevel.Warning)
                .build()

            // 接收相机状态推送（连接时及状态变化时触发）
            connection.on('ReceiveCameraStateAsync', (state: CameraStateDto) => {
                if (!state || !state.cameraId) return
                cameraStates.value.set(state.cameraId, state)
                // 状态变化时刷新对应相机完整数据（保持旧逻辑：列表项与详情同步）
                void refreshCamera(state.cameraId)
            })

            // 接收实时运行指标（每 500ms 推送）
            connection.on('ReceiveLiveMetricsAsync', (cameraId: string, metrics: LiveMetricsWire) => {
                liveMetrics.value.set(cameraId, normalizeLiveMetrics(metrics))
            })

            // 接收 GenICam 节点增量变更推送（Set 节点或 Selector 切换触发）
            connection.on('OnGenICamNodesChangedAsync', (cameraId: string, changes: GenICamNodeChangeDto[]) => {
                const nodeMap = nodeMaps.value.get(cameraId)
                if (!nodeMap || !changes?.length) return
                for (const change of changes) {
                    const node = nodeMap.allNodes.find((n) => n.nodeName === change.nodeName)
                    if (!node) continue
                    if (change.value !== null) node.currentValue = change.value
                    if (change.access !== null) node.access = change.access
                    node.isLocked = change.isLocked
                }
            })

            // 接收 NodeMap 整体重新枚举通知（RefreshNodeMap 后），前端重新拉取完整表
            connection.on('OnGenICamNodeMapReloadedAsync', (cameraId: string, _enumeratedAt: string) => {
                void fetchNodeMap(cameraId)
            })

            connection.onclose(() => {
                hubConnected.value = false
                _stopHeartbeat()
            })
            connection.onreconnected(async () => {
                hubConnected.value = true
                _startHeartbeat()
                // 重连后批量恢复所有已订阅相机的状态（触发 Reattach + 读取最新快照）
                for (const id of subscribedCameraIds.value) {
                    try {
                        await loadCameraSnapshotState(id)
                    } catch {
                        // 忽略单台相机恢复失败，不影响其他
                    }
                }
            })
        }

        try {
            await connection.start()
            hubConnected.value = true
            _startHeartbeat()
        } catch (error) {
            hubConnected.value = false
            connection = null
            throw error
        }
    }

    /** 停止 SignalR Hub 连接并释放所有预览帧资源 */
    async function stopHub() {
        _stopHeartbeat()
        if (connection) {
            await connection.stop()
            connection = null
            hubConnected.value = false
        }
        // 关闭所有相机 MJPEG 流读取器 + 释放 Blob URL
        for (const id of [..._streamAbortCtrls.keys()]) {
            _stopStreamReader(id)
        }
    }

    // ─── 查询 ─────────────────────────────────────────────────────────────────

    /** 加载全部相机列表 */
    async function fetchList(params: GetCameraListDto = {}) {
        loading.value = true
        try {
            const result = await getCameraList({ maxResultCount: 100, ...params })
            cameras.value = [...result.items]
        } finally {
            loading.value = false
        }
    }

    /** 刷新单台相机 */
    async function refreshCamera(id: string) {
        const updated = await getCamera(id)
        _applyUpdate(updated)
    }

    /** 选中相机（切换控制目标） */
    function selectCamera(id: string) {
        selectedCamera.value = cameras.value.find((c) => c.id === id) ?? null
    }

    // ─── CRUD ─────────────────────────────────────────────────────────────────

    async function update(id: string, dto: UpdateCameraDeviceDto): Promise<CameraDeviceDto> {
        const updated = await updateCamera(id, dto)
        _applyUpdate(updated)
        return updated
    }

    // ─── 设备连接控制 ─────────────────────────────────────────────────────────

    async function open(id: string) {
        await openCamera(id)
        await refreshCamera(id)
    }

    async function close(id: string) {
        await closeCamera(id)
        await refreshCamera(id)
    }

    async function scan(): Promise<CameraScanResultDto> {
        const result = await scanCameras()
        await fetchList()
        return result
    }

    // ─── 手动控制：参数读写 ───────────────────────────────────────────────────

    async function fetchDeviceInfo(id: string): Promise<CameraDeviceInfoDto> {
        return await getDeviceInfo(id)
    }

    async function fetchImageRotationAngle(id: string): Promise<number> {
        return await getCameraImageRotationAngle(id)
    }

    async function applyImageRotationAngle(id: string, angle: number): Promise<void> {
        await setCameraImageRotationAngle(id, angle)
        await refreshCamera(id)
    }

    // ─── 预览控制 ─────────────────────────────────────────────────────────────

    async function snapshot(id: string): Promise<CameraSnapshotDto> {
        return await takeSnapshot(id)
    }

    /**
     * 启动相机预览：
     *  1. 确保 SignalR 已连接（用于状态/指标推送和宽限期续约）
     *  2. 调用 REST API 启动采集（相机线程开始抓帧并写入帧缓冲）
     *  3. 启动 fetch + ReadableStream 读取器：自行按 boundary 分帧，组装 BMP Blob URL 写入 previewFrames
     */
    async function startCameraPreview(id: string, dto: StartCameraPreviewDto): Promise<void> {
        await startHub()
        const connectionId = connection?.connectionId ?? dto.connectionId

        await startPreview(id, { ...dto, connectionId })
        // 启动 HTTP MJPEG 流读取器（异步，不阻塞这里）
        void _startStreamReader(id)
        await refreshCamera(id)
    }

    /**
     * 停止相机预览：
     *  1. 调用 REST API 停止采集
     *  2. Abort() 底层 fetch 连接 + revoke 当前 Blob URL
     */
    async function stopCameraPreview(id: string): Promise<void> {
        await stopPreview(id)
        _stopStreamReader(id)
        await refreshCamera(id)
    }

    async function softTrigger(id: string): Promise<void> {
        await doSoftwareTrigger(id)
    }

    async function exposureAutoOncePulse(id: string): Promise<void> {
        await doExposureAutoOncePulse(id)
    }

    async function fetchRtpEndpoint(id: string): Promise<CameraRtpEndpointDto> {
        return await getRtpEndpoint(id)
    }

    // ─── 通用 GenICam 节点读写 ───────────────────────────────────────────────

    async function batchGetGenICamParams(id: string, nodes: GenICamNodeGetInput[]): Promise<GenICamBatchGetResultDto> {
        return apiBatchGetGenICamParams(id, nodes)
    }

    async function setGenICamParam(id: string, input: GenICamNodeSetInput): Promise<void> {
        await apiSetGenICamParam(id, input)

        // 写入成功后，利用依赖图本地更新受影响节点的 Access，无需重新枚举
        const nodeMap = nodeMaps.value.get(id)
        if (!nodeMap) return

        // 只处理整数/枚举类型的节点（float/string 不会是选择器）
        if (input.dataType !== 'int' && input.dataType !== 'enum') return

        const newIntValue = parseInt(input.value, 10)
        if (isNaN(newIntValue)) return

        // 找到所有 selectorNode = 该节点且 optionValue = 新选中值的依赖边
        const affectedEdges = nodeMap.dependencies.filter(
            (dep) => dep.selectorNode === input.nodeName && dep.optionValue === newIntValue && dep.newAccess != null
        )
        if (affectedEdges.length === 0) return

        // 构建受影响节点的最新 access 映射（同一选项下可能有多条边指向同一节点，取最后一条）
        const accessMap = new Map<string, string>()
        for (const edge of affectedEdges) {
            if (edge.newAccess) accessMap.set(edge.affectedNode, edge.newAccess)
        }

        // normalize 后 allNodes 和 categories.nodes 共享引用，只需更新 allNodes 即可
        for (const node of nodeMap.allNodes) {
            const newAccess = accessMap.get(node.nodeName)
            if (newAccess) node.access = newAccess
        }
    }

    async function executeGenICamCommand(id: string, nodeName: string): Promise<void> {
        await apiExecuteGenICamCommand(id, nodeName)
    }

    // ─── 动态 NodeMap ─────────────────────────────────────────────────────────

    /**
     * 让 categories.nodes 和 allNodes 共享同一对象引用。
     * JSON 反序列化后两者是独立副本，normalize 后修改 allNodes[x] 即可同时反映到 categories。
     */
    function normalizeNodeMap(map: CameraNodeMapDto): CameraNodeMapDto {
        const index = new Map(map.allNodes.map((n) => [n.nodeName, n]))
        for (const cat of map.categories) {
            cat.nodes = cat.nodes.map((n) => index.get(n.nodeName) ?? n)
        }
        return map
    }

    /** 拉取并缓存指定相机的 NodeMap 快照 */
    async function fetchNodeMap(id: string): Promise<CameraNodeMapDto> {
        const map = normalizeNodeMap(await apiGetCameraNodeMap(id))
        nodeMaps.value.set(id, map)
        return map
    }

    /** 强制刷新 NodeMap（重新枚举 + 探测依赖） */
    async function refreshNodeMap(id: string): Promise<CameraNodeMapDto> {
        const map = normalizeNodeMap(await apiRefreshCameraNodeMap(id))
        nodeMaps.value.set(id, map)
        return map
    }

    /** 批量读取节点最新值（用于 Selector 切换后局部刷新） */
    async function readNodes(id: string, nodes: GenICamNodeGetInput[]): Promise<GenICamBatchGetResultDto> {
        return apiReadCameraNodes(id, nodes)
    }

    /**
     * 页面刷新/路由返回/SignalR 重连后调用：
     * 一次往返拉取相机完整运行状态快照，恢复 NodeMap 缓存、预览状态等 UI 状态。
     * 同时向 Hub 发送 ReattachPreview，续约宽限期内的预览会话。
     *
     * 注意：若相机在预览中，这里也会同步设置 HTTP MJPEG 预览 URL，
     * 避免刷新后画面丢失。
     */
    async function loadCameraSnapshotState(id: string): Promise<CameraSnapshotStateDto> {
        await startHub()

        // 向 Hub 续约预览（取消宽限期停止）；若未在宽限期内则静默忽略
        try {
            if (connection?.state === signalR.HubConnectionState.Connected) {
                await connection.invoke<boolean>('ReattachPreviewAsync', id)
            }
        } catch {
            // 忽略续约失败
        }

        const snapshot = await apiGetCameraSnapshotState(id)

        // 恢复 NodeMap 缓存
        if (snapshot.nodeMap) {
            nodeMaps.value.set(id, normalizeNodeMap(snapshot.nodeMap))
        }

        // 若快照显示正在预览，立即启动 MJPEG 流读取器
        if (snapshot.isPreviewing) {
            void _startStreamReader(id)
        }

        // 记录订阅，持久化以便重连时批量恢复
        subscribedCameraIds.value.add(id)
        _saveSubscribedIds()

        return snapshot
    }

    return {
        // 状态
        cameras,
        selectedCamera,
        loading,
        hubConnected,
        previewFrames,
        liveMetrics,
        cameraStates,
        nodeMaps,
        subscribedCameraIds,
        // Hub
        startHub,
        stopHub,
        // 查询
        fetchList,
        refreshCamera,
        selectCamera,
        // CRUD
        update,
        // 设备控制
        open,
        close,
        scan,
        // 参数读写
        fetchDeviceInfo,
        fetchImageRotationAngle,
        applyImageRotationAngle,
        // 预览
        snapshot,
        startCameraPreview,
        stopCameraPreview,
        softTrigger,
        exposureAutoOncePulse,
        fetchRtpEndpoint,
        // GenICam 通用节点读写
        batchGetGenICamParams,
        setGenICamParam,
        executeGenICamCommand,
        // 动态 NodeMap
        fetchNodeMap,
        refreshNodeMap,
        readNodes,
        loadCameraSnapshotState,
    }
})
