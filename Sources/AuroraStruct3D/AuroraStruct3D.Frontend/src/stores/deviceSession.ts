/**
 * 设备独占会话 Pinia Store
 *
 * 维护所有设备的当前占用状态，通过 SignalR 实时更新。
 * 使用 deviceId (string) 作为 Map key。
 */
import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
    type DeviceSessionDto,
    type DeviceSessionChangedDto,
    DeviceSessionAction,
    DeviceType,
    acquireSession,
    forceAcquireSession,
    releaseSession,
    getAllSessions,
} from '@/api/device-sessions'
import { getClientSessionId } from '@/utils/clientSession'

export const useDeviceSessionStore = defineStore('deviceSession', () => {
    // ─── 状态 ───────────────────────────────────────────────────────────────

    /** deviceId → 当前会话信息；未被占用的设备不在 Map 中 */
    const sessions = ref<Map<string, DeviceSessionDto>>(new Map())

    // ─── 内部辅助 ────────────────────────────────────────────────────────────

    /** 处理 SignalR 推送的会话变更事件（由 deviceState store 调用） */
    function handleSessionChanged(dto: DeviceSessionChangedDto): void {
        if (dto.action === DeviceSessionAction.Released) {
            if (dto.oldOccupant) {
                sessions.value.delete(dto.oldOccupant.deviceId)
            }
        } else {
            // Acquired 或 ForceTaken
            if (dto.newOccupant) {
                sessions.value.set(dto.newOccupant.deviceId, dto.newOccupant)
            }
        }
    }

    /** 接收连接时的全量会话快照（由 deviceState store 调用） */
    function handleAllSessions(list: DeviceSessionDto[]): void {
        const map = new Map<string, DeviceSessionDto>()
        for (const s of list) {
            map.set(s.deviceId, s)
        }
        sessions.value = map
    }

    // ─── 查询工具 ────────────────────────────────────────────────────────────

    /** 判断指定设备是否已被任何人占用 */
    function isOccupied(deviceId: string): boolean {
        return sessions.value.has(deviceId)
    }

    /** 判断指定设备是否被当前标签页占用 */
    function isOccupiedByMe(deviceId: string): boolean {
        const s = sessions.value.get(deviceId)
        return s?.clientSessionId === getClientSessionId()
    }

    /** 获取指定设备的当前占用信息；未被占用时返回 undefined */
    function getOccupant(deviceId: string): DeviceSessionDto | undefined {
        return sessions.value.get(deviceId)
    }

    // ─── Actions ─────────────────────────────────────────────────────────────

    /** 从后端拉取全量会话（用于手动同步） */
    async function fetchAll(): Promise<void> {
        const list = await getAllSessions()
        handleAllSessions(list)
    }

    /** 获取设备独占权（非强制）；失败时（被他人占用）抛出异常 */
    async function acquire(deviceId: string, deviceType: DeviceType): Promise<DeviceSessionDto> {
        const result = await acquireSession(deviceId, deviceType)
        sessions.value.set(result.deviceId, result)
        return result
    }

    /** 强制接管设备独占权 */
    async function forceAcquire(deviceId: string, deviceType: DeviceType): Promise<DeviceSessionDto> {
        const result = await forceAcquireSession(deviceId, deviceType)
        sessions.value.set(result.deviceId, result)
        return result
    }

    /** 释放当前标签页持有的设备独占权 */
    async function release(deviceId: string): Promise<void> {
        await releaseSession(deviceId)
        // 乐观更新（正式状态由 SignalR Released 事件同步）
        if (isOccupiedByMe(deviceId)) {
            sessions.value.delete(deviceId)
        }
    }

    return {
        sessions,
        handleSessionChanged,
        handleAllSessions,
        isOccupied,
        isOccupiedByMe,
        getOccupant,
        fetchAll,
        acquire,
        forceAcquire,
        release,
    }
})
