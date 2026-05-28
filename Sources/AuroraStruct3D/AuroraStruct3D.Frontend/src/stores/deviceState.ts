/**
 * 设备状态 Pinia Store
 *
 * 负责：
 *  1. 通过匿名 SignalR Hub（/signalr-hubs/device-state）接收实时推送
 *  2. 在 App.vue 启动时连接，关闭时断开
 *  3. 提供设备状态/故障的响应式数据供所有组件使用
 */
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import {
    type DeviceStateDto,
    type DeviceFaultDto,
    DeviceStatus,
    DeviceStatusLabels,
    DeviceRunModeLabels,
    DeviceFaultLevelLabels,
} from '@/api/device-state'
import { getClientSessionId } from '@/utils/clientSession'
import { type DeviceSessionChangedDto, type DeviceSessionDto } from '@/api/device-sessions'

export const useDeviceStateStore = defineStore('deviceState', () => {
    // ─── 状态 ─────────────────────────────────────────────────────────────────
    const state = ref<DeviceStateDto | null>(null)
    const currentFault = ref<DeviceFaultDto | null>(null)
    const isConnected = ref(false)

    let connection: signalR.HubConnection | null = null

    // ─── 计算属性 ─────────────────────────────────────────────────────────────

    const statusLabel = computed<string>(() => {
        if (state.value === null) return '—'
        return DeviceStatusLabels[state.value.status] ?? String(state.value.status)
    })

    const runModeLabel = computed<string>(() => {
        if (state.value === null) return '—'
        return DeviceRunModeLabels[state.value.runMode] ?? String(state.value.runMode)
    })

    const faultLevelLabel = computed<string | null>(() => {
        if (state.value?.currentFaultLevel == null) return null
        return DeviceFaultLevelLabels[state.value.currentFaultLevel] ?? null
    })

    /** 状态对应的语义色调（用于徽章着色） */
    const statusVariant = computed<'default' | 'secondary' | 'destructive' | 'outline'>(() => {
        const s = state.value?.status
        if (s === undefined || s === null) return 'outline'
        if (s === DeviceStatus.Running) return 'default'
        if (s === DeviceStatus.Fault || s === DeviceStatus.EmergencyStop) return 'destructive'
        if (s === DeviceStatus.Standby || s === DeviceStatus.Paused || s === DeviceStatus.Stopped) return 'secondary'
        return 'outline'
    })

    // ─── SignalR 连接管理 ──────────────────────────────────────────────────────

    /** 启动 Hub 连接（匿名，无 token） */
    async function connectAsync(): Promise<void> {
        if (connection) return // 防止重复连接

        connection = new signalR.HubConnectionBuilder()
            .withUrl(`/signalr-hubs/device-state?clientSessionId=${getClientSessionId()}`) // 上报 per-tab 会话标识
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build()

        // 接收设备状态快照推送
        connection.on('ReceiveDeviceStateAsync', (dto: DeviceStateDto) => {
            state.value = dto
        })

        // 接收当前故障推送
        connection.on('ReceiveDeviceFaultAsync', (dto: DeviceFaultDto | null) => {
            currentFault.value = dto
        })

        // 接收设备操作会话变更通知，转发给 deviceSession store
        connection.on('ReceiveDeviceSessionChangedAsync', (dto: DeviceSessionChangedDto) => {
            // 懒加载避免循环依赖：在回调内 import 而非顶层
            import('@/stores/deviceSession').then(({ useDeviceSessionStore }) => {
                useDeviceSessionStore().handleSessionChanged(dto)
            })
        })

        // 接收连接时推送的全量会话快照
        connection.on('ReceiveAllDeviceSessionsAsync', (list: DeviceSessionDto[]) => {
            import('@/stores/deviceSession').then(({ useDeviceSessionStore }) => {
                useDeviceSessionStore().handleAllSessions(list)
            })
        })

        connection.onreconnected(() => {
            isConnected.value = true
        })

        connection.onclose(() => {
            isConnected.value = false
        })

        try {
            await connection.start()
            isConnected.value = true
        } catch (err) {
            console.warn('[DeviceStateStore] SignalR 连接失败:', err)
            isConnected.value = false
        }
    }

    /** 停止 Hub 连接 */
    async function disconnectAsync(): Promise<void> {
        if (!connection) return
        try {
            await connection.stop()
        } finally {
            connection = null
            isConnected.value = false
        }
    }

    return {
        // 状态
        state,
        currentFault,
        isConnected,
        // 计算属性
        statusLabel,
        runModeLabel,
        faultLevelLabel,
        statusVariant,
        // 方法
        connectAsync,
        disconnectAsync,
    }
})
