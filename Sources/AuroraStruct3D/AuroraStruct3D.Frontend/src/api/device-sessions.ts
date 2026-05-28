/**
 * 设备独占操作会话 API
 * 对应后端 DeviceSessionAppService（ABP 动态 API 路由自动生成）
 */
import { httpClient } from '@/api/client'

// ─── 类型定义 ─────────────────────────────────────────────────────────────────

/** 设备类型（与后端 DeviceType 枚举保持一致） */
export enum DeviceType {
    Camera = 0,
    Projector = 1,
    Motor = 2,
}

/** 会话变更动作 */
export enum DeviceSessionAction {
    Acquired = 0,
    Released = 1,
    ForceTaken = 2,
}

/** 设备独占会话 DTO */
export interface DeviceSessionDto {
    deviceId: string
    deviceType: DeviceType
    clientSessionId: string
    occupantUserId: string | null
    occupantUserName: string
    acquiredAt: string
    expiresAt: string | null
}

/** 会话变更通知（来自 SignalR） */
export interface DeviceSessionChangedDto {
    action: DeviceSessionAction
    newOccupant: DeviceSessionDto | null
    oldOccupant: DeviceSessionDto | null
}

// ─── API 调用 ─────────────────────────────────────────────────────────────────

const BASE = '/api/app/device-session'

/** 获取所有当前活跃的设备占用会话 */
export async function getAllSessions(): Promise<DeviceSessionDto[]> {
    const res = await httpClient.get<DeviceSessionDto[]>(`${BASE}`)
    return res.data
}

/** 查询指定设备的当前占用会话；未被占用时返回 null */
export async function getSession(deviceId: string): Promise<DeviceSessionDto | null> {
    const res = await httpClient.get<DeviceSessionDto | null>(`${BASE}/${deviceId}`)
    return res.data
}

/**
 * 获取指定设备的独占操作权（非强制）。
 * 若设备已被其他标签页占用，后端返回 HTTP 409。
 */
export async function acquireSession(deviceId: string, deviceType: DeviceType): Promise<DeviceSessionDto> {
    const res = await httpClient.post<DeviceSessionDto>(`${BASE}/acquire`, { deviceId, deviceType })
    return res.data
}

/**
 * 强制接管指定设备的独占操作权（踢出当前占用者）。
 * 若设备为相机且正在预览，后端会先停止预览再接管。
 */
export async function forceAcquireSession(deviceId: string, deviceType: DeviceType): Promise<DeviceSessionDto> {
    const res = await httpClient.post<DeviceSessionDto>(`${BASE}/force-acquire`, { deviceId, deviceType })
    return res.data
}

/** 释放当前标签页持有的设备操作权（非 owner 调用时静默忽略） */
export async function releaseSession(deviceId: string): Promise<void> {
    await httpClient.post(`${BASE}/release`, { deviceId })
}
