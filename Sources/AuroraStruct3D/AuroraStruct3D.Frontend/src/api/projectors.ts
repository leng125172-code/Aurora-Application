/**
 * 投影仪设备手动控制 REST API
 *
 * 路由前缀：/api/projectors
 */
import { httpClient } from '@/api/client'
import type { PagedResultDto } from '@/api/device-state'

// ─── 枚举（与后端 C# enum 保持一致）────────────────────────────────────────

export enum ProjectorConnectionType {
    Tcp = 0,
    UsbHid = 1,
}

export enum ProjectorConnectionStatus {
    Unknown = 0,
    Disconnected = 1,
    Connected = 2,
    ConnectionFailed = 3,
}

export enum ProjectorLedStatus {
    Unknown = 0,
    Off = 1,
    On = 2,
}

export enum ProjectorColor {
    Red = 0,
    Green = 1,
    Blue = 2,
    White = 3,
    AuraSync = 4,
}

export enum ProjectorDisplayMode {
    Black = 0,
    White = 1,
    Cross = 2,
    Checkerboard = 3,
    Internal1 = 6,
    Internal2 = 7,
}

export enum ProjectorFlipMode {
    None = 0,
    FlipX = 1,
    FlipY = 2,
    FlipXY = 3,
}

export enum ProjectorTriggerMode {
    Normal = 0,
    Loop = 1,
    SingleFrame = 2,
}

export enum ProjectorBootImage {
    Black = 0,
    White = 1,
    Cross = 2,
    Checkerboard = 3,
    Internal1 = 6,
    Internal2 = 7,
}

// ─── DTO 类型 ────────────────────────────────────────────────────────────────

export interface ProjectorDeviceDto {
    readonly id: string
    readonly creationTime: string
    readonly creatorId: string | null
    readonly lastModificationTime: string | null
    readonly lastModifierId: string | null
    readonly isDeleted: boolean
    readonly deletionTime: string | null
    readonly deleterId: string | null

    readonly name: string
    readonly deviceIndex: number
    readonly description: string | null
    readonly isEnabled: boolean

    readonly connectionType: ProjectorConnectionType
    readonly ipAddress: string | null
    readonly tcpPort: number
    readonly hidDeviceIndex: number
    readonly connectTimeoutMs: number

    readonly deviceHardwareId: number

    readonly connectionStatus: ProjectorConnectionStatus
    readonly connectionStatusText: string
    readonly ledStatus: ProjectorLedStatus
    readonly ledStatusText: string
    readonly lastLightValue: number
    readonly lastDisplayMode: number
    readonly lastColor: ProjectorColor
    readonly checkerboardPixelSize: number
    readonly flipMode: ProjectorFlipMode
    readonly triggerMode: ProjectorTriggerMode
    readonly bootImage: ProjectorBootImage
    readonly ledRgbR: number
    readonly ledRgbG: number
    readonly ledRgbB: number
    readonly lastCommunicationAt: string | null
    readonly lastConnectedAt: string | null
    readonly lastDisconnectedAt: string | null
}

export interface ProjectorOperationLogDto {
    readonly id: string
    readonly projectorDeviceId: string
    readonly operationType: number
    readonly isSuccess: boolean
    readonly rawCommand: string | null
    readonly parameterSummary: string | null
    readonly errorMessage: string | null
    readonly roundTripMs: number
    readonly occurredAt: string
}

// ─── 输入 DTO ────────────────────────────────────────────────────────────────

export interface UpdateProjectorDeviceDto {
    name: string
    description?: string
    isEnabled: boolean
    /** USB HID projector identity stored in user register 0 (1-255). */
    deviceHardwareId?: number | null
}

export interface GetProjectorListDto {
    filter?: string
    isEnabled?: boolean
    skipCount?: number
    maxResultCount?: number
}

export interface GetProjectorLogListDto {
    projectorDeviceId?: string
    operationType?: number
    isFailedOnly?: boolean
    startTime?: string
    endTime?: string
    skipCount?: number
    maxResultCount?: number
}

export interface SetProjectorLightDto {
    projectorDeviceId: string
    light: number
}

export interface SetProjectorDisplayModeDto {
    projectorDeviceId: string
    mode: ProjectorDisplayMode
}

export interface SetProjectorColorDto {
    projectorDeviceId: string
    color: ProjectorColor
}

export interface SetProjectorFlipDto {
    projectorDeviceId: string
    flipMode: ProjectorFlipMode
}

export interface SetProjectorTriggerModeDto {
    projectorDeviceId: string
    triggerMode: ProjectorTriggerMode
}

export interface SetProjectorBootImageDto {
    projectorDeviceId: string
    bootImage: ProjectorBootImage
}

export interface SetProjectorCheckerboardDto {
    projectorDeviceId: string
    pixelSize: number
}

export interface SetProjectorRgbDto {
    projectorDeviceId: string
    r: number
    g: number
    b: number
}

export interface TriggerProjectorDto {
    projectorDeviceId: string
    endGray?: number
}

export interface NextProjectorFrameDto {
    projectorDeviceId: string
}

export interface WriteProjectorRegisterDto {
    projectorDeviceId: string
    address: number
    value: number
}

// ─── API 函数 ────────────────────────────────────────────────────────────────

const BASE = '/api/app/projector-device'

/** 分页查询投影仪列表 */
export async function getProjectorList(params: GetProjectorListDto = {}): Promise<PagedResultDto<ProjectorDeviceDto>> {
    const { data } = await httpClient.get<PagedResultDto<ProjectorDeviceDto>>(BASE, { params })
    return data
}

/** 查询单台投影仪 */
export async function getProjector(id: string): Promise<ProjectorDeviceDto> {
    const { data } = await httpClient.get<ProjectorDeviceDto>(`${BASE}/${id}`)
    return data
}

/** 更新投影仪信息 */
export async function updateProjector(id: string, dto: UpdateProjectorDeviceDto): Promise<ProjectorDeviceDto> {
    const { data } = await httpClient.put<ProjectorDeviceDto>(`${BASE}/${id}`, dto)
    return data
}

/** 扫描 USB HID 投影仪，自动同步数据库记录，返回检测到的数量 */
export async function scanProjectors(): Promise<number> {
    const { data } = await httpClient.post<number>(`${BASE}/scan-projectors`)
    return data
}

// ─── 连接 ────────────────────────────────────────────────────────────────────

/** 连接投影仪 */
export async function connectProjector(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/connect`)
}

/** 断开投影仪 */
export async function disconnectProjector(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/disconnect`)
}

// ─── LED 控制 ─────────────────────────────────────────────────────────────────

/** 开灯 */
export async function projectorLedOn(id: string): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/${id}/led-on`)
    return data
}

/** 关灯 */
export async function projectorLedOff(id: string): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/${id}/led-off`)
    return data
}

/** 设置亮度 */
export async function setProjectorLight(dto: SetProjectorLightDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-light`, dto)
    return data
}

// ─── 显示控制 ─────────────────────────────────────────────────────────────────

/** 设置显示模式 */
export async function setProjectorDisplayMode(dto: SetProjectorDisplayModeDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-display-mode`, dto)
    return data
}

/** 设置颜色（多光谱） */
export async function setProjectorColor(dto: SetProjectorColorDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-color`, dto)
    return data
}

/** 设置翻转模式 */
export async function setProjectorFlip(dto: SetProjectorFlipDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-flip`, dto)
    return data
}

/** 设置触发模式 */
export async function setProjectorTriggerMode(dto: SetProjectorTriggerModeDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-trigger-mode`, dto)
    return data
}

/** 设置开机图案 */
export async function setProjectorBootImage(dto: SetProjectorBootImageDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-boot-image`, dto)
    return data
}

/** 设置棋盘格像素尺寸 */
export async function setProjectorCheckerboard(dto: SetProjectorCheckerboardDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-checkerboard-pixel-size`, dto)
    return data
}

/** 设置 RGB 彩光亮度 */
export async function setProjectorRgb(dto: SetProjectorRgbDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/set-rgb-color`, dto)
    return data
}

// ─── 触发 ────────────────────────────────────────────────────────────────────

/** 触发一次投影 */
export async function triggerProjectorOnce(dto: TriggerProjectorDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/trigger-once`, dto)
    return data
}

/** 单帧触发模式下切换到下一张条纹（发送 N 指令） */
export async function nextFrameProjector(dto: NextProjectorFrameDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/next-frame`, dto)
    return data
}

// ─── 高级 ────────────────────────────────────────────────────────────────────

/** 软复位 */
export async function projectorSoftReset(id: string): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/${id}/soft-reset`)
    return data
}

/** 保存参数到 NVM */
export async function projectorSaveParams(id: string): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/${id}/save-params`)
    return data
}

/** 读取寄存器 */
export async function readProjectorRegister(id: string, address: number): Promise<string | null> {
    const { data } = await httpClient.post<string | null>(`${BASE}/${id}/read-register`, { address })
    return data
}

/** 写入寄存器 */
export async function writeProjectorRegister(dto: WriteProjectorRegisterDto): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/write-register`, dto)
    return data
}

// ─── 日志 ────────────────────────────────────────────────────────────────────

/** 查询操作日志 */
export async function getProjectorLogs(
    params: GetProjectorLogListDto = {}
): Promise<PagedResultDto<ProjectorOperationLogDto>> {
    const { data } = await httpClient.get<PagedResultDto<ProjectorOperationLogDto>>(`${BASE}/logs`, { params })
    return data
}

// ─── 条纹图（结构光标定） ──────────────────────────────────────────────────────

/** 通过 Fp 指令读取投影仪像素分辨率，返回宽度像素和像素模式 */
export interface ProjectorPixelResolutionDto {
    widthPixels: number
    pixelMode: string
}

/** 条纹方向：horizontal=横条纹 vertical=竖条纹 */
export type FringeMode = 'horizontal' | 'vertical'

/** 条纹类型：bw=黑白（首色黑）wb=白黑（首色白） */
export type FringeType = 'bw' | 'wb'

/** 横条纹填充位置：end=后置黑色填充（默认），start=前置黑色填充 */
export type HorizontalPaddingPosition = 'start' | 'end'

/** 下载条纹图像到光机 Flash 的输入参数 */
export interface DownloadFringePatternInput {
    projectorId: string
    /** 条纹方向 */
    fringeMode: FringeMode
    /** 条纹类型 */
    fringeType: FringeType
    darkLevel: number
    brightLevel: number
    /** 投影宽度像素（通过 Fp 指令读取） */
    widthPixels: number
    /** 投影高度像素（用户设置） */
    heightPixels: number
    /** 兼容旧接口；固定条纹配置下不参与图像计算 */
    periodCount: number
    /** 兼容旧接口；固定条纹配置下不参与图像计算 */
    imageCount: number
    /** 兼容旧接口；固定配置下不参与计算，互补图直接逐像素取反 */
    phaseShift: number
    /** 横条纹帧黑色填充位置（调试用，默认 end） */
    horizontalPaddingPosition?: HorizontalPaddingPosition
}

export interface FringePreviewImageDto {
    index: number
    label: string
    /** 后端 byte[] 经 JSON 序列化后的 Base64 字符串 */
    pixels: string
}

export type ProjectorFringeDownloadStatus = 'Idle' | 'Running' | 'Completed' | 'Failed'

export interface ProjectorFringeDownloadStatusDto {
    projectorId: string
    status: ProjectorFringeDownloadStatus
    progress: number
    errorMessage: string | null
}

/** 通过 Fp 指令读取投影仪像素分辨率 */
export async function getProjectorPixelResolution(id: string): Promise<ProjectorPixelResolutionDto> {
    const { data } = await httpClient.get<ProjectorPixelResolutionDto>(`${BASE}/${id}/pixel-resolution`)
    return data
}

/** 在后端生成条纹图像预览数据并返回前端 */
export async function generateFringePreview(input: DownloadFringePatternInput): Promise<FringePreviewImageDto[]> {
    const { data } = await httpClient.post<FringePreviewImageDto[]>(`${BASE}/generate-fringe-preview`, input)
    return data
}

/** 查询投影仪当前条纹下载状态 */
export async function getProjectorFringeDownloadStatus(id: string): Promise<ProjectorFringeDownloadStatusDto> {
    const { data } = await httpClient.get<ProjectorFringeDownloadStatusDto>(`${BASE}/${id}/fringe-download-status`)
    return data
}

/** 触发将条纹图像下载到光机 Flash（后台生成并写入，通过 SignalR 推送进度） */
export async function downloadFringePattern(input: DownloadFringePatternInput): Promise<void> {
    await httpClient.post(`${BASE}/download-fringe-pattern`, input)
}
