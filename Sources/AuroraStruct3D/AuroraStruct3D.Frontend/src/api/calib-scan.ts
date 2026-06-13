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

/** 启动扫描输入 */
export interface StartCalibScanInput {
    calibProjectId: string
}

/** 停止扫描输入 */
export interface StopCalibScanInput {
    calibProjectId: string
}

/** 实时指标 */
export interface CalibScanMetricsDto {
    fps: number
    depthValidRate: number
    confidence: number
    depthMapDataUri: string | null
    frameIndex: number
    timestamp: string
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

/** 设置图像增强开关输入 */
export interface SetCalibScanImageEnhanceInput {
    calibProjectId: string
    enabled: boolean
}

/** 设置 OpenCV CLAHE 图像增强开关 */
export async function setCalibScanImageEnhance(input: SetCalibScanImageEnhanceInput): Promise<void> {
    await httpClient.post(`${BASE}/set-image-enhance`, input)
}
