<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Checkbox from 'primevue/checkbox'
import { showErrorToastOnce } from '@/api/client'
import { useAppToast } from '@/composables/useAppToast'
import type { CalibProjectDto } from '@/api/calibration'
import {
    CalibScanRunState,
    getCalibScanStatus,
    startCalibScan,
    stopCalibScan,
    setCalibScanImageEnhance,
    type CalibScanStatusDto,
} from '@/api/calib-scan'

const props = defineProps<{
    project: CalibProjectDto
}>()

const { success: toastSuccess } = useAppToast()

const loading = ref(false)
const status = ref<CalibScanStatusDto | null>(null)
const hubConnected = ref(false)
const reconnecting = ref(false)
const conflictHint = ref<string | null>(null)
const imageEnhanceEnabled = ref(false)
let hubConnection: signalR.HubConnection | null = null

const stateText = computed(() => {
    switch (status.value?.state) {
        case CalibScanRunState.Starting:
            return '启动中'
        case CalibScanRunState.Running:
            return '运行中'
        case CalibScanRunState.Stopping:
            return '停止中'
        case CalibScanRunState.Failed:
            return '运行失败'
        default:
            return '未运行'
    }
})

const stateSeverity = computed<'success' | 'info' | 'warn' | 'danger' | 'secondary'>(() => {
    switch (status.value?.state) {
        case CalibScanRunState.Running:
            return 'success'
        case CalibScanRunState.Starting:
        case CalibScanRunState.Stopping:
            return 'info'
        case CalibScanRunState.Failed:
            return 'danger'
        default:
            return 'secondary'
    }
})

const canStart = computed(() => {
    return !loading.value && !status.value?.isRunning
})

const canStop = computed(() => {
    return !loading.value && !!status.value?.isRunning
})

const depthMapDataUri = computed(() => {
    return status.value?.latestMetrics?.depthMapDataUri ?? null
})

async function startHub(): Promise<void> {
    if (hubConnection?.state === signalR.HubConnectionState.Connected) {
        return
    }

    if (hubConnection && hubConnection.state !== signalR.HubConnectionState.Disconnected) {
        return
    }

    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .configureLogging(signalR.LogLevel.Warning)
        .build()

    hubConnection.onreconnecting(() => {
        reconnecting.value = true
        hubConnected.value = false
    })

    hubConnection.onreconnected(async () => {
        reconnecting.value = false
        hubConnected.value = true
        try {
            await hubConnection?.invoke('JoinCalibScanGroupAsync', props.project.id)
        } catch {
            // 忽略分组恢复失败
        }
        void refreshStatus(false)
    })

    hubConnection.onclose(() => {
        reconnecting.value = false
        hubConnected.value = false
    })

    hubConnection.on('ReceiveCalibScanStateAsync', (next: CalibScanStatusDto) => {
        if (!next || next.calibProjectId !== props.project.id) {
            return
        }
        status.value = next
    })

    hubConnection.on(
        'ReceiveCalibScanMetricsAsync',
        (projectId: string, metrics: CalibScanStatusDto['latestMetrics']) => {
            if (projectId !== props.project.id || !metrics) {
                return
            }

            const current = status.value
            if (!current) {
                return
            }

            status.value = {
                ...current,
                latestMetrics: metrics,
                lastUpdatedAt: metrics.timestamp,
            }
        }
    )

    try {
        await hubConnection.start()
        hubConnected.value = true
        reconnecting.value = false
        await hubConnection.invoke('JoinCalibScanGroupAsync', props.project.id)
    } catch (e) {
        hubConnected.value = false
        showErrorToastOnce(e)
    }
}

async function stopHub(): Promise<void> {
    if (!hubConnection) {
        return
    }

    try {
        await hubConnection.invoke('LeaveCalibScanGroupAsync', props.project.id)
        await hubConnection.stop()
    } finally {
        hubConnection = null
        hubConnected.value = false
        reconnecting.value = false
    }
}

async function refreshStatus(showLoading = true): Promise<void> {
    if (showLoading) {
        loading.value = true
    }
    try {
        const res = await getCalibScanStatus(props.project.id)
        status.value = res
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        if (showLoading) {
            loading.value = false
        }
    }
}

async function onStart(): Promise<void> {
    loading.value = true
    conflictHint.value = null
    try {
        const res = await startCalibScan({
            calibProjectId: props.project.id,
        })
        status.value = res
        toastSuccess('已启动在线扫描会话')
    } catch (e) {
        const code = (e as { response?: { data?: { error?: { code?: string } } } })?.response?.data?.error?.code
        const message = (e as { response?: { data?: { error?: { message?: string } } } })?.response?.data?.error
            ?.message
        const statusCode = (e as { response?: { status?: number } })?.response?.status
        if (code === 'AuroraStruct3D:DeviceOccupied' || statusCode === 409) {
            conflictHint.value = message ?? '设备当前被其他会话占用，请关闭其他页面或等待会话释放后重试。'
        }
        showErrorToastOnce(e)
    } finally {
        loading.value = false
    }
}

async function onStop(): Promise<void> {
    loading.value = true
    try {
        const res = await stopCalibScan({ calibProjectId: props.project.id })
        status.value = res
        toastSuccess('已停止在线扫描会话')
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        loading.value = false
    }
}

async function onImageEnhanceChange(): Promise<void> {
    try {
        await setCalibScanImageEnhance({
            calibProjectId: props.project.id,
            enabled: imageEnhanceEnabled.value,
        })
    } catch (e) {
        imageEnhanceEnabled.value = !imageEnhanceEnabled.value
        showErrorToastOnce(e)
    }
}

onMounted(async () => {
    await startHub()
    await refreshStatus(true)
})

onUnmounted(async () => {
    await stopHub()
})
</script>

<template>
    <div class="flex flex-1 min-h-0 flex-col gap-4 px-4 py-4 overflow-auto">
        <div class="rounded-xl border border-border/60 bg-card/40 p-4">
            <div class="mb-3 flex items-center justify-between">
                <h3 class="text-base font-semibold">Step6 在线扫描</h3>
                <Tag :value="stateText" :severity="stateSeverity" />
            </div>

            <div class="mb-3 flex flex-wrap gap-2">
                <Tag :severity="hubConnected ? 'success' : 'warn'" :value="hubConnected ? 'Hub已连接' : 'Hub未连接'" />
                <Tag v-if="reconnecting" severity="info" value="重连中..." />
            </div>

            <div class="grid grid-cols-1 gap-3 md:grid-cols-3">
                <div class="md:col-span-3 flex items-end gap-2">
                    <Button
                        label="启动扫描"
                        icon="pi pi-play"
                        :loading="loading"
                        :disabled="!canStart"
                        @click="onStart"
                    />
                    <Button
                        label="停止扫描"
                        severity="secondary"
                        outlined
                        icon="pi pi-stop"
                        :loading="loading"
                        :disabled="!canStop"
                        @click="onStop"
                    />
                    <Button
                        label="刷新状态"
                        text
                        icon="pi pi-refresh"
                        :loading="loading"
                        @click="refreshStatus(true)"
                    />
                </div>
            </div>

            <div class="mt-3 flex items-center gap-2">
                <Checkbox v-model="imageEnhanceEnabled" input-id="imageEnhance" binary @change="onImageEnhanceChange" />
                <label for="imageEnhance" class="text-sm cursor-pointer select-none">
                    启用 OpenCV 图像增强（CLAHE 自适应对比度优化）
                </label>
            </div>

            <p v-if="status?.errorMessage" class="mt-3 text-sm text-red-400">
                {{ status.errorMessage }}
            </p>
            <p v-if="conflictHint" class="mt-2 text-sm text-amber-300">会话冲突：{{ conflictHint }}</p>
            <p class="mt-2 text-sm text-muted-foreground">
                双 USB 相机场景受底层驱动限制，不走同时预览；Step6 采用后台轮询抓拍主从相机并生成深度图。
            </p>
        </div>

        <div class="rounded-xl border border-border/60 bg-card/40 p-4">
            <h4 class="mb-3 text-sm font-semibold">实时指标（MVP 占位）</h4>
            <div class="grid grid-cols-2 gap-3 md:grid-cols-5">
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">FPS</div>
                    <div class="text-lg font-semibold">{{ status?.latestMetrics?.fps?.toFixed(2) ?? '0.00' }}</div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">深度有效率</div>
                    <div class="text-lg font-semibold">
                        {{ ((status?.latestMetrics?.depthValidRate ?? 0) * 100).toFixed(2) }}%
                    </div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">置信度</div>
                    <div class="text-lg font-semibold">
                        {{ ((status?.latestMetrics?.confidence ?? 0) * 100).toFixed(2) }}%
                    </div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">帧序号</div>
                    <div class="text-lg font-semibold">{{ status?.latestMetrics?.frameIndex ?? 0 }}</div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">最后更新时间</div>
                    <div class="text-xs font-medium pt-1 break-all">{{ status?.lastUpdatedAt ?? '-' }}</div>
                </div>
            </div>
        </div>

        <div class="rounded-xl border border-border/60 bg-card/40 p-4">
            <h4 class="mb-3 text-sm font-semibold">实时深度图（后台抓拍重建）</h4>
            <div class="rounded-lg border border-border/50 p-2">
                <div class="mb-2 text-xs text-muted-foreground">Depth Map</div>
                <div class="aspect-video w-full overflow-hidden rounded bg-black/70">
                    <img
                        v-if="depthMapDataUri"
                        :src="depthMapDataUri"
                        alt="depth-map-preview"
                        class="h-full w-full object-contain"
                    />
                    <div v-else class="flex h-full items-center justify-center text-xs text-muted-foreground">
                        暂无深度图，请启动扫描并等待首帧重建
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
