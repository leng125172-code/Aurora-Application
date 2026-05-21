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
    Hdr = 0,
    High = 1,
    Low = 2,
}

export enum CameraBinningMode {
    Off = 0,
    X2 = 1,
    X4 = 2,
}

export enum CameraPixelDepth {
    Bit8 = 0,
    Bit12 = 1,
}

export enum CameraWhiteBalanceMode {
    Manual = 0,
    Once = 1,
    Continuous = 2,
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

    readonly model: string | null
    readonly serialNumber: string | null
    readonly firmwareVersion: string | null
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

export interface CameraImageParamsDto {
    roiEnabled: boolean
    roiHOffset: number
    roiVOffset: number
    roiWidth: number
    roiHeight: number
    pixelDepth: CameraPixelDepth
    horizontalFlip: boolean
    verticalFlip: boolean
    binning: CameraBinningMode
    gammaEnabled: boolean
    gamma: number
    contrast: number
    brightness: number
    frameRate: number
    frameRateMax: number
}

export interface SetCameraImageParamsDto {
    roiEnabled?: boolean
    roiHOffset?: number
    roiVOffset?: number
    roiWidth?: number
    roiHeight?: number
    pixelDepth?: CameraPixelDepth
    horizontalFlip?: boolean
    verticalFlip?: boolean
    binning?: CameraBinningMode
    gammaEnabled?: boolean
    gamma?: number
    contrast?: number
    brightness?: number
    frameRate?: number
}

export interface CameraAcquisitionParamsDto {
    aeMode: CameraAutoExposureMode
    aeStatus: number
    aeTargetGray: number
    aeMaxExposure: number
    aeMinExposure: number
    gainMode: CameraGainMode
    exposureTime: number
    globalGain: number
}

export interface SetCameraAcquisitionParamsDto {
    aeMode?: CameraAutoExposureMode
    aeTargetGray?: number
    aeMaxExposure?: number
    aeMinExposure?: number
    gainMode?: CameraGainMode
    exposureTime?: number
    globalGain?: number
}

export interface CameraTriggerOutDto {
    port: number
    mode: number
    edgeMode: number
    delayTm: number
    width: number
}

export interface CameraTriggerParamsDto {
    triggerMode: number
    expMode: number
    edgeMode: number
    delayTm: number
    frames: number
    bufFrames: number
    triggerOut1: CameraTriggerOutDto
    triggerOut2: CameraTriggerOutDto
    triggerOut3: CameraTriggerOutDto
}

export interface SetCameraTriggerParamsDto {
    triggerMode?: number
    expMode?: number
    edgeMode?: number
    delayTm?: number
    frames?: number
    bufFrames?: number
    triggerOut1?: CameraTriggerOutDto
    triggerOut2?: CameraTriggerOutDto
    triggerOut3?: CameraTriggerOutDto
}

export interface CameraCalcRoiDto {
    enabled: boolean
    hOffset: number
    vOffset: number
    width: number
    height: number
}

export interface CameraCustomParamsDto {
    wbMode: CameraWhiteBalanceMode
    channelGainR: number
    channelGainG: number
    channelGainB: number
    saturation: number
    colorTemperature: number
    wbCalcRoi: CameraCalcRoiDto
    ledEnabled: boolean
    currentBufFrames: number
}

export interface SetCameraCustomParamsDto {
    wbMode?: CameraWhiteBalanceMode
    channelGainR?: number
    channelGainG?: number
    channelGainB?: number
    saturation?: number
    colorTemperature?: number
    wbCalcRoi?: CameraCalcRoiDto
    ledEnabled?: boolean
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
}

export interface CameraUserProfileDto {
    profileName: string
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

// ─── 图像参数 ─────────────────────────────────────────────────────────────────

/** 获取图像采集参数 */
export async function getCameraImageParams(id: string): Promise<CameraImageParamsDto> {
    const { data } = await httpClient.get<CameraImageParamsDto>(`${BASE}/${id}/image-params`)
    return data
}

/** 设置图像采集参数 */
export async function setCameraImageParams(id: string, dto: SetCameraImageParamsDto): Promise<CameraImageParamsDto> {
    const { data } = await httpClient.post<CameraImageParamsDto>(`${BASE}/${id}/image-params`, dto)
    return data
}

// ─── 采集参数 ─────────────────────────────────────────────────────────────────

/** 获取采集参数 */
export async function getCameraAcquisitionParams(id: string): Promise<CameraAcquisitionParamsDto> {
    const { data } = await httpClient.get<CameraAcquisitionParamsDto>(`${BASE}/${id}/acquisition-params`)
    return data
}

/** 设置采集参数 */
export async function setCameraAcquisitionParams(
    id: string,
    dto: SetCameraAcquisitionParamsDto
): Promise<CameraAcquisitionParamsDto> {
    const { data } = await httpClient.post<CameraAcquisitionParamsDto>(`${BASE}/${id}/acquisition-params`, dto)
    return data
}

// ─── 触发参数 ─────────────────────────────────────────────────────────────────

/** 获取触发参数 */
export async function getCameraTriggerParams(id: string): Promise<CameraTriggerParamsDto> {
    const { data } = await httpClient.get<CameraTriggerParamsDto>(`${BASE}/${id}/trigger-params`)
    return data
}

/** 设置触发参数 */
export async function setCameraTriggerParams(
    id: string,
    dto: SetCameraTriggerParamsDto
): Promise<CameraTriggerParamsDto> {
    const { data } = await httpClient.post<CameraTriggerParamsDto>(`${BASE}/${id}/trigger-params`, dto)
    return data
}

// ─── 自定义参数 ───────────────────────────────────────────────────────────────

/** 获取自定义参数 */
export async function getCameraCustomParams(id: string): Promise<CameraCustomParamsDto> {
    const { data } = await httpClient.get<CameraCustomParamsDto>(`${BASE}/${id}/custom-params`)
    return data
}

/** 设置自定义参数 */
export async function setCameraCustomParams(id: string, dto: SetCameraCustomParamsDto): Promise<CameraCustomParamsDto> {
    const { data } = await httpClient.post<CameraCustomParamsDto>(`${BASE}/${id}/custom-params`, dto)
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

/** 获取 RTP 推流端点信息 */
export async function getRtpEndpoint(id: string): Promise<CameraRtpEndpointDto> {
    const { data } = await httpClient.get<CameraRtpEndpointDto>(`${BASE}/${id}/rtp-endpoint`)
    return data
}

// ─── 用户配置文件 ─────────────────────────────────────────────────────────────

/** 加载用户配置文件 */
export async function loadUserProfile(id: string, dto: CameraUserProfileDto): Promise<void> {
    await httpClient.post(`${BASE}/${id}/load-user-profile`, dto)
}

/** 保存用户配置文件 */
export async function saveUserProfile(id: string, dto: CameraUserProfileDto): Promise<void> {
    await httpClient.post(`${BASE}/${id}/save-user-profile`, dto)
}
