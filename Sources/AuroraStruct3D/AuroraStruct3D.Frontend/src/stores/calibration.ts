/**
 * 标定管理 Pinia Store
 *
 * 功能：
 *  1. 管理 SignalR 与 /signalr-hubs/calibration 的连接生命周期
 *  2. 实时接收标定计算进度（ReceiveCalibrationProgressAsync）和完成通知（ReceiveCalibrationCompletedAsync）
 *  3. 以 projectId 为键缓存当前进度与完成状态，供 CalibrationWizardPage 订阅
 */
import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'

/** 标定计算进度快照 */
export interface CalibrationProgress {
    projectId: string
    stage: string
    percent: number
    message: string | null
}

/** 标定计算完成通知 */
export interface CalibrationCompletion {
    projectId: string
    success: boolean
    resultId: string | null
    errorMessage: string | null
}

export const useCalibrationStore = defineStore('calibration', () => {
    // ─── 状态 ─────────────────────────────────────────────────────────────────

    /** SignalR 连接状态 */
    const hubConnected = ref(false)
    /** 各工程的实时计算进度（key = projectId） */
    const progressMap = ref<Map<string, CalibrationProgress>>(new Map())
    /** 各工程的计算完成通知（key = projectId） */
    const completionMap = ref<Map<string, CalibrationCompletion>>(new Map())

    let connection: signalR.HubConnection | null = null

    // ─── SignalR ──────────────────────────────────────────────────────────────

    /** 启动 SignalR 连接，幂等调用 */
    async function startHub(): Promise<void> {
        if (connection?.state === signalR.HubConnectionState.Connected) {
            hubConnected.value = true
            return
        }
        if (connection && connection.state !== signalR.HubConnectionState.Disconnected) {
            return
        }

        if (!connection) {
            connection = new signalR.HubConnectionBuilder()
                .withUrl('/signalr-hubs/calibration', { skipNegotiation: false })
                .withAutomaticReconnect([0, 2000, 5000, 10000])
                .configureLogging(signalR.LogLevel.Warning)
                .build()

            /** 接收标定计算阶段进度推送 */
            connection.on(
                'ReceiveCalibrationProgressAsync',
                (projectId: string, stage: string, percent: number, message: string | null) => {
                    progressMap.value.set(projectId, { projectId, stage, percent, message })
                }
            )

            /** 接收标定计算完成或失败通知 */
            connection.on(
                'ReceiveCalibrationCompletedAsync',
                (
                    projectId: string,
                    success: boolean,
                    resultId: string | null,
                    errorMessage: string | null
                ) => {
                    completionMap.value.set(projectId, { projectId, success, resultId, errorMessage })
                }
            )

            connection.onclose(() => {
                hubConnected.value = false
            })
            connection.onreconnected(() => {
                hubConnected.value = true
            })
        }

        await connection.start()
        hubConnected.value = true
    }

    /** 停止 SignalR 连接 */
    async function stopHub(): Promise<void> {
        if (connection) {
            await connection.stop()
            hubConnected.value = false
        }
    }

    // ─── 数据访问 ─────────────────────────────────────────────────────────────

    /** 获取指定工程的最新进度 */
    function getProgress(projectId: string): CalibrationProgress | undefined {
        return progressMap.value.get(projectId)
    }

    /** 获取指定工程的完成通知 */
    function getCompletion(projectId: string): CalibrationCompletion | undefined {
        return completionMap.value.get(projectId)
    }

    /** 清除指定工程的进度与完成状态（重新计算前调用） */
    function clearProjectProgress(projectId: string): void {
        progressMap.value.delete(projectId)
        completionMap.value.delete(projectId)
    }

    return {
        hubConnected,
        progressMap,
        completionMap,
        startHub,
        stopHub,
        getProgress,
        getCompletion,
        clearProjectProgress,
    }
})
