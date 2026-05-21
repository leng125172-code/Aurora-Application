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
    type CameraImageParamsDto,
    type SetCameraImageParamsDto,
    type CameraAcquisitionParamsDto,
    type SetCameraAcquisitionParamsDto,
    type CameraTriggerParamsDto,
    type SetCameraTriggerParamsDto,
    type CameraCustomParamsDto,
    type SetCameraCustomParamsDto,
    type CameraLiveMetricsDto,
    type CameraRtpEndpointDto,
    type CameraSnapshotDto,
    type CameraUserProfileDto,
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
    getCameraImageParams as getImageParams,
    setCameraImageParams as setImageParams,
    getCameraAcquisitionParams as getAcquisitionParams,
    setCameraAcquisitionParams as setAcquisitionParams,
    getCameraTriggerParams as getTriggerParams,
    setCameraTriggerParams as setTriggerParams,
    getCameraCustomParams as getCustomParams,
    setCameraCustomParams as setCustomParams,
    takeSnapshot,
    startPreview,
    stopPreview,
    doSoftwareTrigger,
    getRtpEndpoint,
    loadUserProfile,
    saveUserProfile,
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

    let connection: signalR.HubConnection | null = null

    // ─── 辅助函数 ─────────────────────────────────────────────────────────────

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

    /** 启动 SignalR Hub 连接（使用 MessagePack 协议，二进制帧无 base64 开销） */
    async function startHub() {
        if (connection) return

        connection = new signalR.HubConnectionBuilder()
            .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
            .withHubProtocol(new MessagePackHubProtocol())
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build()

        // 接收相机状态推送（连接时及状态变化时触发）
        connection.on('ReceiveCameraStateAsync', (cameraId: string, _state: string) => {
            // 状态变化时刷新对应相机完整数据
            void refreshCamera(cameraId)
        })

        // 接收 JPEG 预览帧（30FPS 限速）
        connection.on('ReceiveCameraFrameAsync', (cameraId: string, frame: Uint8Array) => {
            // 释放旧的 ObjectURL，避免内存泄漏
            const oldUrl = previewFrames.value.get(cameraId)
            if (oldUrl) URL.revokeObjectURL(oldUrl)

            const blob = new Blob([frame.buffer as ArrayBuffer], { type: 'image/jpeg' })
            const url = URL.createObjectURL(blob)
            previewFrames.value.set(cameraId, url)
        })

        // 接收实时运行指标（每 2 秒推送）
        connection.on('ReceiveLiveMetricsAsync', (cameraId: string, metrics: CameraLiveMetricsDto) => {
            liveMetrics.value.set(cameraId, metrics)
        })

        connection.onclose(() => {
            hubConnected.value = false
        })
        connection.onreconnected(() => {
            hubConnected.value = true
        })

        try {
            await connection.start()
            hubConnected.value = true
        } catch {
            hubConnected.value = false
        }
    }

    /** 停止 SignalR Hub 连接并释放所有预览帧资源 */
    async function stopHub() {
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

    async function fetchImageParams(id: string): Promise<CameraImageParamsDto> {
        return await getImageParams(id)
    }

    async function applyImageParams(id: string, dto: SetCameraImageParamsDto): Promise<void> {
        await setImageParams(id, dto)
    }

    async function fetchAcquisitionParams(id: string): Promise<CameraAcquisitionParamsDto> {
        return await getAcquisitionParams(id)
    }

    async function applyAcquisitionParams(id: string, dto: SetCameraAcquisitionParamsDto): Promise<void> {
        await setAcquisitionParams(id, dto)
    }

    async function fetchTriggerParams(id: string): Promise<CameraTriggerParamsDto> {
        return await getTriggerParams(id)
    }

    async function applyTriggerParams(id: string, dto: SetCameraTriggerParamsDto): Promise<void> {
        await setTriggerParams(id, dto)
    }

    async function fetchCustomParams(id: string): Promise<CameraCustomParamsDto> {
        return await getCustomParams(id)
    }

    async function applyCustomParams(id: string, dto: SetCameraCustomParamsDto): Promise<void> {
        await setCustomParams(id, dto)
    }

    // ─── 预览控制 ─────────────────────────────────────────────────────────────

    async function snapshot(id: string): Promise<CameraSnapshotDto> {
        return await takeSnapshot(id)
    }

    async function startCameraPreview(id: string, dto: StartCameraPreviewDto): Promise<void> {
        await startPreview(id, dto)
    }

    async function stopCameraPreview(id: string): Promise<void> {
        await stopPreview(id)
        // 清除对应相机的预览帧
        const url = previewFrames.value.get(id)
        if (url) URL.revokeObjectURL(url)
        previewFrames.value.delete(id)
    }

    async function softTrigger(id: string): Promise<void> {
        await doSoftwareTrigger(id)
    }

    async function fetchRtpEndpoint(id: string): Promise<CameraRtpEndpointDto> {
        return await getRtpEndpoint(id)
    }

    // ─── 用户配置文件 ─────────────────────────────────────────────────────────

    async function loadProfile(id: string, dto: CameraUserProfileDto): Promise<void> {
        await loadUserProfile(id, dto)
    }

    async function saveProfile(id: string, dto: CameraUserProfileDto): Promise<void> {
        await saveUserProfile(id, dto)
    }

    return {
        // 状态
        cameras,
        selectedCamera,
        loading,
        hubConnected,
        previewFrames,
        liveMetrics,
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
        fetchImageParams,
        applyImageParams,
        fetchAcquisitionParams,
        applyAcquisitionParams,
        fetchTriggerParams,
        applyTriggerParams,
        fetchCustomParams,
        applyCustomParams,
        // 预览
        snapshot,
        startCameraPreview,
        stopCameraPreview,
        softTrigger,
        fetchRtpEndpoint,
        // 配置文件
        loadProfile,
        saveProfile,
    }
})
