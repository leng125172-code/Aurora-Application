/**
 * 相机设备管理及手动控制 REST API
 *
 * 路由前缀：/api/app/camera-device（ABP 动态 API 约定）
 */
import { httpClient } from '@/api/client'
import type { PagedResultDto } from '@/api/device-state'

// ─── 枚举（与后端 C# enum 保持一致）────────────────────────────────────────

export enum CameraStatus {
    Unknown = 0,
    Ready = 1,
    Capturing = 2,
    Error = 3,
    Closed = 4,
}

export enum CameraAutoExposureMode {
    Off = 0,
    Once = 1,
    Continuous = 2,
}

export enum CameraGainMode {
    HighCapacity = 0,
    Balanced = 1,
    Sensitive = 2,
}

export enum CameraBinningMode {
    Off = 0,
    X2 = 1,
    X4 = 2,
}

export enum CameraPixelDepth {
    HighDepth12bit = 0,
    Speed8bit = 1,
}

export enum CameraWhiteBalanceMode {
    Manual = 0,
    Automatic = 1,
    Preset = 2,
}

// ─── 基础 DTO ────────────────────────────────────────────────────────────────

export interface CameraDeviceDto {
    readonly id: string
    readonly creationTime: string
    readonly creatorId: string | null
    readonly lastModificationTime: string | null
    readonly lastModifierId: string | null

    readonly name: string
    readonly deviceIndex: number
    readonly description: string | null
    readonly isEnabled: boolean
    readonly status: CameraStatus
    readonly statusText: string
    readonly imageRotationAngle: number

    readonly model: string | null
}

// ─── 管理用输入 DTO ──────────────────────────────────────────────────────────

export interface UpdateCameraDeviceDto {
    name: string
    description?: string
    isEnabled: boolean
}

export interface GetCameraListDto {
    filter?: string
    isEnabled?: boolean
    skipCount?: number
    maxResultCount?: number
}

// ─── 手动控制 DTO ────────────────────────────────────────────────────────────

export interface CameraDeviceInfoDto {
    readonly model: string
    readonly serialNumber: string
    readonly firmwareVersion: string
    readonly fpgaVersion: string
    readonly fpgaTemperature: number
    readonly sensorTemperature: number
    readonly currentWidth: number
    readonly currentHeight: number
}

export interface CameraSnapshotDto {
    readonly dataUri: string
    readonly capturedAt: string
}

export interface StartCameraPreviewDto {
    connectionId?: string
    enableRtp?: boolean
}

export interface CameraRtpEndpointDto {
    readonly endpoint: string
    readonly sdpBase64: string
}

export interface CameraLiveMetricsDto {
    readonly fpgaTemperature: number
    readonly sensorTemperature: number
    readonly frameRate: number
    readonly aeStatus: number
    readonly currentBufFrames: number
    /** 对焦清晰度评分（0-100，越高越清晰） */
    readonly focusScore: number
    /** 曝光质量评分（0-100，越高曝光越适中） */
    readonly apertureScore: number
    /** 光圈调节建议：0=良好, 1=缩小光圈, 2=增大光圈 */
    readonly apertureHint: number
}

// ─── API 函数 ────────────────────────────────────────────────────────────────

const BASE = '/api/app/camera-device'

// ─── 相机管理 ─────────────────────────────────────────────────────────────────

/** 分页查询相机列表 */
export async function getCameraList(params: GetCameraListDto = {}): Promise<PagedResultDto<CameraDeviceDto>> {
    const { data } = await httpClient.get<PagedResultDto<CameraDeviceDto>>(BASE, { params })
    return data
}

/** 查询单台相机 */
export async function getCamera(id: string): Promise<CameraDeviceDto> {
    const { data } = await httpClient.get<CameraDeviceDto>(`${BASE}/${id}`)
    return data
}

/** 更新相机基本信息 */
export async function updateCamera(id: string, dto: UpdateCameraDeviceDto): Promise<CameraDeviceDto> {
    const { data } = await httpClient.put<CameraDeviceDto>(`${BASE}/${id}`, dto)
    return data
}

/** 扫描相机（同步SDK与数据库） */
export async function scanCameras(): Promise<number> {
    const { data } = await httpClient.post<number>(`${BASE}/scan-cameras`)
    return data
}

/** 打开相机 */
export async function openCamera(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/open-camera`)
}

/** 关闭相机 */
export async function closeCamera(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/close-camera`)
}

// ─── 设备信息 ─────────────────────────────────────────────────────────────────

/** 获取相机硬件设备信息 */
export async function getCameraDeviceInfo(id: string): Promise<CameraDeviceInfoDto> {
    const { data } = await httpClient.get<CameraDeviceInfoDto>(`${BASE}/${id}/device-info`)
    return data
}

// ─── 快照与预览 ───────────────────────────────────────────────────────────────

/** 单帧快照 */
export async function takeSnapshot(id: string): Promise<CameraSnapshotDto> {
    const { data } = await httpClient.post<CameraSnapshotDto>(`${BASE}/${id}/take-snapshot`)
    return data
}

/** 开始实时预览 */
export async function startPreview(id: string, dto: StartCameraPreviewDto = {}): Promise<void> {
    await httpClient.post(`${BASE}/${id}/start-preview`, dto)
}

/** 停止实时预览 */
export async function stopPreview(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/stop-preview`)
}

/** 发送软件触发 */
export async function doSoftwareTrigger(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/do-software-trigger`)
}

/** 触发单次自动曝光（ExposureAutoOncePulse 命令节点） */
export async function doExposureAutoOncePulse(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/do-exposure-auto-once-pulse`)
}

/** 获取 RTP 推流端点信息 */
export async function getRtpEndpoint(id: string): Promise<CameraRtpEndpointDto> {
    const { data } = await httpClient.get<CameraRtpEndpointDto>(`${BASE}/${id}/rtp-endpoint`)
    return data
}

// ─── GenICam 通用节点读写 ─────────────────────────────────────────────────────

/** GenICam 单节点读取请求 */
export interface GenICamNodeGetInput {
    nodeName: string
    /** 'int' | 'float' | 'string' */
    dataType: string
}

/** GenICam 单节点读取结果 */
export interface GenICamNodeResultDto {
    nodeName: string
    /** 统一字符串化的值；null 表示读取失败 */
    value: string | null
    success: boolean
    errorMessage?: string | null
    /** 节点当前访问模式（ReadOnly / ReadWrite / WriteOnly 等）；读取失败时为 null */
    access?: string | null
}

/** GenICam 批量读取结果 */
export interface GenICamBatchGetResultDto {
    results: GenICamNodeResultDto[]
}

/** GenICam 单节点写入请求 */
export interface GenICamNodeSetInput {
    nodeName: string
    /** 'int' | 'float' | 'string' */
    dataType: string
    value: string
}

/** 读取单个 GenICam 节点值 */
export async function getGenICamParam(id: string, input: GenICamNodeGetInput): Promise<GenICamNodeResultDto> {
    const { data } = await httpClient.get<GenICamNodeResultDto>(`${BASE}/${id}/gen-iCam-param`, {
        params: input,
    })
    return data
}

/** 批量读取多个 GenICam 节点值 */
export async function batchGetGenICamParams(
    id: string,
    nodes: GenICamNodeGetInput[]
): Promise<GenICamBatchGetResultDto> {
    const { data } = await httpClient.post<GenICamBatchGetResultDto>(`${BASE}/${id}/batch-get-gen-iCam-params`, {
        nodes,
    })
    return data
}

/** 写入单个 GenICam 节点值 */
export async function setGenICamParam(id: string, input: GenICamNodeSetInput): Promise<void> {
    await httpClient.post(`${BASE}/${id}/set-gen-iCam-param`, input)
}

/** 执行 GenICam 命令节点 */
export async function executeGenICamCommand(id: string, nodeName: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/execute-gen-iCam-command`, nodeName, {
        headers: { 'Content-Type': 'application/json' },
    })
}

// ─── 动态 GenICam NodeMap（前端基于此动态渲染参数面板）──────────────────────

/** GenICam 枚举条目 DTO */
export interface GenICamEnumEntryDto {
    value: number
    symbolic: string
    displayName: string
    isAvailable: boolean
}

/** GenICam 节点 DTO（完整元信息 + 首次枚举值快照） */
export interface GenICamNodeDto {
    nodeName: string
    displayName: string
    xmlScope: number
    level: number
    /** 节点类型：Integer / Float / Enumeration / Boolean / String / Command / Category 等 */
    nodeType: string
    /** 访问模式：ReadOnly / WriteOnly / ReadWrite / NotImplemented / NotAvailable */
    access: string
    /** 可见性：Beginner / Expert / Guru / Invisible */
    visibility: string
    representation: number
    unit?: string | null
    description?: string | null
    isLocked: boolean
    intMin: number
    intMax: number
    intStep: number
    floatMin: number
    floatMax: number
    floatStep: number
    currentValue: string | null
    enumEntries: GenICamEnumEntryDto[]
    pollingTime: number
    displayPrecision: number
}

/** GenICam Category 分组 DTO */
export interface GenICamCategoryDto {
    name: string
    displayName: string
    nodes: GenICamNodeDto[]
}

/** Selector → AffectedNode 依赖项 */
export interface GenICamDependencyDto {
    selectorNode: string
    optionValue: number
    optionLabel: string
    affectedNode: string
    changeSummary: string
    /** 切换到该选项后，受影响节点的新 Access 状态（如 "ReadOnly" / "ReadWrite"）；无 Access 变化时为 null */
    newAccess?: string | null
}

/** 相机 NodeMap 完整快照 */
export interface CameraNodeMapDto {
    cameraId: string
    enumeratedAt: string
    categories: GenICamCategoryDto[]
    allNodes: GenICamNodeDto[]
    dependencies: GenICamDependencyDto[]
}

/** 批量节点读取请求 */
export interface GenICamBatchGetInput {
    nodes: GenICamNodeGetInput[]
}

/** 获取相机当前缓存的 GenICam NodeMap（若首次仍在预跑会返回空骨架） */
export async function getCameraNodeMap(id: string): Promise<CameraNodeMapDto> {
    const { data } = await httpClient.get<CameraNodeMapDto>(`${BASE}/${id}/node-map`)
    return data
}

/** 强制重新枚举 GenICam NodeMap 并重新探测依赖 */
export async function refreshCameraNodeMap(id: string): Promise<CameraNodeMapDto> {
    const { data } = await httpClient.post<CameraNodeMapDto>(`${BASE}/${id}/refresh-node-map`)
    return data
}

/** 批量读取节点当前值（不刷新 NodeMap，仅查值） */
export async function readCameraNodes(id: string, nodes: GenICamNodeGetInput[]): Promise<GenICamBatchGetResultDto> {
    const { data } = await httpClient.post<GenICamBatchGetResultDto>(`${BASE}/${id}/read-nodes`, {
        nodes,
    })
    return data
}

// ─── 相机实时状态（SignalR 推送 DTO 对应 TypeScript 类型）────────────────────

/** SignalR `ReceiveCameraStateAsync` 推送的完整状态 DTO */
export interface CameraStateDto {
    cameraId: string
    status: number
    statusText: string
    isCapturing: boolean
    isXmlLoaded: boolean
    sensorTemperature: number | null
    fpgaTemperature: number | null
    lastErrorMessage: string | null
    changedAt: string
}

// ─── 图像旋转角度（软件端旋转）──────────────────────────────────────────────

export interface SetCameraRotationAngleDto {
    angle: number
}

export async function getCameraImageRotationAngle(id: string): Promise<number> {
    const res = await httpClient.get<number>(`/api/app/camera-device/${id}/image-rotation-angle`)
    return res.data
}

export async function setCameraImageRotationAngle(id: string, angle: number): Promise<void> {
    await httpClient.put(`/api/app/camera-device/${id}/image-rotation-angle`, { angle })
}

// ─── 页面刷新/重连后一次性恢复 UI 状态 ──────────────────────────────────────

/** GenICam 节点增量变更（SignalR OnGenICamNodesChangedAsync 推送）*/
export interface GenICamNodeChangeDto {
    nodeName: string
    value: string | null
    access: string | null
    isLocked: boolean
}

/** 相机完整快照状态（前端刷新/重连后调用 GetCameraSnapshotStateAsync 恢复 UI）*/
export interface CameraSnapshotStateDto {
    cameraId: string
    nodeMap: CameraNodeMapDto | null
    triggerMode: number
    triggerModeSymbol: string
    isPreviewing: boolean
    isCapturing: boolean
    rtpEndpoint: CameraRtpEndpointDto | null
    cameraStatus: string
    snapshotAt: string
}

/** 获取相机当前完整运行状态快照，用于页面刷新/返回/重连后恢复 UI */
export async function getCameraSnapshotState(id: string): Promise<CameraSnapshotStateDto> {
    const { data } = await httpClient.get<CameraSnapshotStateDto>(`${BASE}/${id}/camera-snapshot-state`)
    return data
}

// ─── 操作日志 ────────────────────────────────────────────────────────────────

/** 相机操作日志 DTO */
export interface CameraOperationLogDto {
    readonly id: string
    readonly cameraDeviceId: string
    readonly deviceIndex: number
    readonly operationType: number
    readonly occurredAt: string
    readonly isSuccess: boolean
    readonly parameterSummary: string | null
    readonly errorMessage: string | null
    readonly roundTripMs: number
}

/** 查询相机操作日志请求参数 */
export interface GetCameraLogListDto {
    cameraDeviceId?: string
    operationType?: number
    isFailedOnly?: boolean
    startTime?: string
    endTime?: string
    skipCount?: number
    maxResultCount?: number
}

/** 分页查询指定相机的操作日志（按发生时间倒序） */
export async function getCameraLogs(params: GetCameraLogListDto = {}): Promise<PagedResultDto<CameraOperationLogDto>> {
    const { data } = await httpClient.get<PagedResultDto<CameraOperationLogDto>>(`${BASE}/logs`, { params })
    return data
}
