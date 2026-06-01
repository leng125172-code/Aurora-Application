/**
 * 相机 Pinia Store
 *
 * 负责：
 *  1. 维护相机设备列表响应式数据
 *  2. 通过 SignalR Hub（/signalr-hubs/camera，MessagePack 协议）
 *     接收实时状态推送、JPEG 帧、运行指标
 *  3. 封装所有手动控制操作
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
    /** 实时 JPEG 预览帧 ObjectURL，key 为相机 ID */
    const previewFrames = ref<Map<string, string>>(new Map())
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

    function normalizeFrameBytes(frame: Uint8Array | ArrayBuffer | number[] | ArrayLike<number>): Uint8Array {
        if (frame instanceof Uint8Array) return Uint8Array.from(frame)
        if (frame instanceof ArrayBuffer) return new Uint8Array(frame.slice(0))
        if (Array.isArray(frame)) return Uint8Array.from(frame)
        if (ArrayBuffer.isView(frame)) {
            const view = frame as ArrayBufferView
            return new Uint8Array(view.buffer.slice(view.byteOffset, view.byteOffset + view.byteLength))
        }
        return Uint8Array.from(frame)
    }

    function toBlobArrayBuffer(bytes: Uint8Array): ArrayBuffer {
        const copy = new Uint8Array(bytes.byteLength)
        copy.set(bytes)
        return copy.buffer
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

    /** 启动 SignalR Hub 连接（使用 MessagePack 协议，二进制帧无 base64 开销） */
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

            // 接收相机状态推送（连接时及状态变化时触发，已迁移到 CameraStateDto 单参数）
            connection.on('ReceiveCameraStateAsync', (state: CameraStateDto) => {
                if (!state || !state.cameraId) return
                cameraStates.value.set(state.cameraId, state)
                // 状态变化时刷新对应相机完整数据（保持旧逻辑：列表项与详情同步）
                void refreshCamera(state.cameraId)
            })

            // 接收 JPEG 预览帧（30FPS 限速）
            connection.on('ReceiveCameraFrameAsync', (cameraId: string, frame: Uint8Array | ArrayBuffer | number[]) => {
                try {
                    const bytes = normalizeFrameBytes(frame)
                    if (bytes.byteLength === 0) return

                    const oldUrl = previewFrames.value.get(cameraId)
                    const blob = new Blob([toBlobArrayBuffer(bytes)], { type: 'image/jpeg' })
                    const url = URL.createObjectURL(blob)
                    previewFrames.value.set(cameraId, url)

                    if (oldUrl) {
                        window.setTimeout(() => URL.revokeObjectURL(oldUrl), 1000)
                    }
                } catch (error) {
                    console.warn('接收相机预览帧失败', error)
                }
            })

            // 接收实时运行指标（每 2 秒推送）
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
        // 释放所有 ObjectURL
        previewFrames.value.forEach((url) => URL.revokeObjectURL(url))
        previewFrames.value.clear()
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

    async function scan(): Promise<number> {
        const count = await scanCameras()
        await fetchList()
        return count
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

    async function startCameraPreview(id: string, dto: StartCameraPreviewDto): Promise<void> {
        await startHub()
        const connectionId = connection?.connectionId ?? dto.connectionId
        if (!connectionId) {
            throw new Error('相机实时连接未就绪，无法启动预览')
        }

        await startPreview(id, { ...dto, connectionId })
        await refreshCamera(id)
    }

    async function stopCameraPreview(id: string): Promise<void> {
        await stopPreview(id)
        // 清除对应相机的预览帧
        const url = previewFrames.value.get(id)
        if (url) URL.revokeObjectURL(url)
        previewFrames.value.delete(id)
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
