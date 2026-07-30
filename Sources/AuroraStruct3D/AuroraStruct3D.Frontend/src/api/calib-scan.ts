/**
 * Step6 在线扫描 REST API
 *
 * 路由前缀：/api/app/calib-scan（ABP 动态 API 约定）
 */
import { httpClient } from '@/api/client'

const BASE = '/api/app/calib-scan'

/** Step6 在线扫描模式 */
export enum CalibScanMode {
    TwoCamera0Light = 0,
    OneCamera1Light = 1,
    TwoCamera1Light = 2,
}

/** Step6 在线扫描运行状态 */
export enum CalibScanRunState {
    Idle = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Failed = 4,
}

/** Step6 扫描相机角色（与后端 CalibScanCameraRole 枚举对齐） */
export enum CalibScanCameraRole {
    /** 主相机 */
    Main = 0,
    /** 从相机 */
    Secondary = 1,
}

/** 启动扫描输入 */
export interface StartCalibScanInput {
    calibProjectId: string
    /** 仅打开投影仪灯光，不发送显示模式、B2、T、N 等控制命令 */
    suppressProjectorControl?: boolean
}

/** 停止扫描输入 */
export interface StopCalibScanInput {
    calibProjectId: string
}

/**
 * 实时指标
 * 注：DepthValidRate / Confidence / DepthMapDataUri 为兼容旧接口保留，
 * 结构光扫描流程中固定为 0 / 0 / null
 */
export interface CalibScanMetricsDto {
    fps: number
    depthValidRate: number
    confidence: number
    depthMapDataUri: string | null
    frameIndex: number
    timestamp: string
    /** 当前轮次序号（从 1 开始递增） */
    roundIndex: number
    /** 当前轮内帧序号（0~patternCount-1） */
    frameIndexInRound: number
    /** 每轮总帧数（已包含横条纹和竖条纹全部帧） */
    patternCount: number
    /** 十字图检测结果（true 表示检测到十字图，本轮播放完毕） */
    isCrosshairDetected: boolean
}

/** 扫描状态 */
export interface CalibScanStatusDto {
    calibProjectId: string
    state: CalibScanRunState
    isRunning: boolean
    startedAt: string | null
    lastUpdatedAt: string
    errorMessage: string | null
    latestMetrics: CalibScanMetricsDto | null
}

/**
 * Step6 扫描单帧图像推送 DTO（SignalR ReceiveCalibScanFrame 事件载荷）
 * 注：jpegBytes 为 MessagePack 二进制传输的 JPEG 原图字节
 */
export interface CalibScanFrameDto {
    calibProjectId: string
    /** 相机角色（0=主相机, 1=从相机） */
    cameraRole: CalibScanCameraRole
    /** JPEG 原图二进制数据 */
    jpegBytes: Uint8Array
    /** 当前轮次序号 */
    roundIndex: number
    /** 当前轮内帧序号 */
    frameIndexInRound: number
}

/** 启动在线扫描 */
export async function startCalibScan(input: StartCalibScanInput): Promise<CalibScanStatusDto> {
    const res = await httpClient.post<CalibScanStatusDto>(`${BASE}/start`, input)
    return res.data
}

/** 停止在线扫描 */
export async function stopCalibScan(input: StopCalibScanInput): Promise<CalibScanStatusDto> {
    const res = await httpClient.post<CalibScanStatusDto>(`${BASE}/stop`, input)
    return res.data
}

/** 获取在线扫描状态 */
export async function getCalibScanStatus(calibProjectId: string): Promise<CalibScanStatusDto> {
    const res = await httpClient.get<CalibScanStatusDto>(`${BASE}/status/${calibProjectId}`)
    return res.data
}
