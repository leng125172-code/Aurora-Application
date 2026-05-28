/**
 * 瓴控 KTECH 电机操作台 Pinia store
 *  - REST：透传 api/ktech.ts 所有方法
 *  - SignalR：订阅 /signalr-hubs/ktech-motor，按 axisId 接收状态/升级进度推送
 */
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import {
    type KtechCapabilitiesDto,
    type KtechProductInfoDto,
    type KtechStateSnapshotDto,
    type KtechUpgradeProgressDto,
} from '@/api/ktech'
import * as ktechApi from '@/api/ktech'

const MAX_LOG = 500

export const useKtechMotorStore = defineStore('ktechMotor', () => {
    /** 各轴最新状态快照（key=axisId） */
    const stateByAxis = ref<Record<string, KtechStateSnapshotDto>>({})
    /** 各轴产品信息（缓存以便切换 Tab 时无需重复请求） */
    const productInfoByAxis = ref<Record<string, KtechProductInfoDto>>({})
    /** 各轴设备能力（按 axisId 缓存；切换电机时按需加载） */
    const capabilitiesByAxis = ref<Record<string, KtechCapabilitiesDto>>({})
    /** 升级进度列表（最近 500 条），按 axisId 分组 */
    const upgradeLogByAxis = ref<Record<string, KtechUpgradeProgressDto[]>>({})
    /** 操作/事件日志（最近 500 条） */
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

    /** 当前 axis 最新状态（响应式） */
    function getState(axisId: string) {
        return computed<KtechStateSnapshotDto | null>(() => stateByAxis.value[axisId] ?? null)
    }

    /** 当前 axis 升级日志 */
    function getUpgradeLog(axisId: string) {
        return computed<KtechUpgradeProgressDto[]>(() => upgradeLogByAxis.value[axisId] ?? [])
    }

    /** 启动 SignalR；可多次 acquire，引用计数到 0 才真正断开。 */
    async function acquireHub() {
        refCount += 1
        if (connection) return
        connection = new signalR.HubConnectionBuilder()
            .withUrl('/signalr-hubs/ktech-motor', { skipNegotiation: false })
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build()

        connection.on('ReceiveKtechMotorStateAsync', (state: KtechStateSnapshotDto) => {
            stateByAxis.value[state.axisId] = state
        })
        connection.on('ReceiveKtechUpgradeProgressAsync', (progress: KtechUpgradeProgressDto) => {
            const list = upgradeLogByAxis.value[progress.axisId] ?? []
            list.push(progress)
            if (list.length > MAX_LOG) list.splice(0, list.length - MAX_LOG)
            upgradeLogByAxis.value[progress.axisId] = list
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
            pushLog('success', '[SignalR] KTECH Hub 已连接')
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
            pushLog('info', '[SignalR] KTECH Hub 已断开')
        }
    }

    function cacheProductInfo(axisId: string, info: KtechProductInfoDto) {
        productInfoByAxis.value[axisId] = info
    }

    /**
     * 获取设备能力（带缓存）。
     * 切换电机时调用一次，UI 用于禁用控件、过滤运动模式、限定输入范围。
     */
    async function ensureCapabilities(axisId: string, force = false): Promise<KtechCapabilitiesDto | null> {
        if (!force && capabilitiesByAxis.value[axisId]) {
            return capabilitiesByAxis.value[axisId]
        }
        try {
            const cap = await ktechApi.getCapabilities(axisId)
            capabilitiesByAxis.value[axisId] = cap
            return cap
        } catch (err) {
            pushLog('warn', `[Capabilities] 加载失败：${(err as Error).message}`)
            return null
        }
    }

    /** 获取能力的响应式引用（未加载时返回 null）。 */
    function getCapabilities(axisId: string) {
        return computed<KtechCapabilitiesDto | null>(() => capabilitiesByAxis.value[axisId] ?? null)
    }

    function clearUpgradeLog(axisId: string) {
        upgradeLogByAxis.value[axisId] = []
    }

    return {
        stateByAxis,
        productInfoByAxis,
        capabilitiesByAxis,
        upgradeLogByAxis,
        logs,
        hubConnected,
        getState,
        getUpgradeLog,
        getCapabilities,
        ensureCapabilities,
        acquireHub,
        releaseHub,
        cacheProductInfo,
        clearUpgradeLog,
        pushLog,
    }
})
