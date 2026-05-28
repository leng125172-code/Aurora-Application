/**
 * 雷赛 iCL-RS 电机操作台 Pinia store
 *  - REST：透传 api/leisai.ts 所有方法
 *  - SignalR：订阅 /signalr-hubs/leisai-motor，按 axisId 接收状态推送，
 *    并维护 120 点滚动缓存供 ECharts 渲染折线图。
 */
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import type { LeisaiStateSnapshotDto, LeisaiTraceDto } from '@/api/leisai'
import * as leisaiApi from '@/api/leisai'

const MAX_LOG = 500

export const useLeisaiMotorStore = defineStore('leisaiMotor', () => {
    /** 各轴最新状态快照（key=axisId） */
    const stateByAxis = ref<Record<string, LeisaiStateSnapshotDto>>({})
    /** 各轴轨迹满动窗口（用于 3D 相位轨迹图，最多 1200 点，由后端全量下发） */
    const traceByAxis = ref<Record<string, LeisaiTraceDto>>({})
    /** 操作日志（最近 500 条） */
    const logs = ref<{ time: string; level: 'info' | 'success' | 'warn' | 'error'; text: string }[]>([])
    /** SignalR 连接状态 */
    const hubConnected = ref(false)

    let connection: signalR.HubConnection | null = null
    let refCount = 0

    function pushLog(level: 'info' | 'success' | 'warn' | 'error', text: string) {
        logs.value.push({ time: new Date().toISOString(), level, text })
        if (logs.value.length > MAX_LOG) {
            logs.value.splice(0, logs.value.length - MAX_LOG)
        }
    }

    function getState(axisId: string) {
        return computed<LeisaiStateSnapshotDto | null>(() => stateByAxis.value[axisId] ?? null)
    }

    function getTrace(axisId: string) {
        return computed<LeisaiTraceDto>(
            () =>
                traceByAxis.value[axisId] ?? {
                    axisId,
                    actualPositions: [],
                    commandPositions: [],
                    speeds: [],
                }
        )
    }

    async function acquireHub() {
        refCount += 1
        if (connection) return
        connection = new signalR.HubConnectionBuilder()
            .withUrl('/signalr-hubs/leisai-motor', { skipNegotiation: false })
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build()

        connection.on('ReceiveLeisaiMotorStateAsync', (state: LeisaiStateSnapshotDto) => {
            stateByAxis.value[state.axisId] = state
        })

        connection.on('ReceiveLeisaiTraceAsync', (trace: LeisaiTraceDto) => {
            traceByAxis.value[trace.axisId] = trace
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
            pushLog('success', '[SignalR] Leisai Hub 已连接')
        } catch (err) {
            hubConnected.value = false
            pushLog('error', `[SignalR] 连接失败：${(err as Error).message}`)
        }
    }

    async function releaseHub() {
        refCount = Math.max(0, refCount - 1)
        if (refCount > 0 || !connection) return
        try {
            await connection.stop()
        } finally {
            connection = null
            hubConnected.value = false
            pushLog('info', '[SignalR] Leisai Hub 已断开')
        }
    }

    return {
        stateByAxis,
        traceByAxis,
        logs,
        hubConnected,
        pushLog,
        getState,
        getTrace,
        acquireHub,
        releaseHub,
        // REST 透传
        api: leisaiApi,
    }
})
