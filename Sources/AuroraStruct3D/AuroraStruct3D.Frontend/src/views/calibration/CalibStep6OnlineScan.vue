<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import { showErrorToastOnce } from '@/api/client'
import { useAppToast } from '@/composables/useAppToast'
import type { CalibProjectDto } from '@/api/calibration'
import {
    CalibScanCameraRole,
    CalibScanRunState,
    getCalibScanStatus,
    startCalibScan,
    stopCalibScan,
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

// 主/从相机原图 Blob URL（每帧覆盖，旧 URL 在更新前 revoke）
const mainImageUrl = ref<string | null>(null)
const secondaryImageUrl = ref<string | null>(null)

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

const metrics = computed(() => status.value?.latestMetrics ?? null)

/**
 * 将 SignalR 推送的 JPEG 字节转换为 Blob URL 供 <img> 显示。
 * 调用方需在覆盖前 revoke 旧 URL，避免内存泄漏。
 */
function bytesToImageUrl(bytes: Uint8Array): string {
    // MessagePack 协议下 byte[] 会以 Uint8Array 形式到达。
    // 复制到独立的 ArrayBuffer，避免 TS 5.7+ 严格类型下 Uint8Array<ArrayBufferLike>
    // 与 BlobPart（要求 ArrayBuffer，不允许 SharedArrayBuffer）类型不兼容问题。
    const buffer = new ArrayBuffer(bytes.byteLength)
    new Uint8Array(buffer).set(bytes)
    const blob = new Blob([buffer], { type: 'image/jpeg' })
    return URL.createObjectURL(blob)
}

function revokeUrl(slot: 'main' | 'secondary'): void {
    if (slot === 'main' && mainImageUrl.value) {
        URL.revokeObjectURL(mainImageUrl.value)
        mainImageUrl.value = null
    }
    if (slot === 'secondary' && secondaryImageUrl.value) {
        URL.revokeObjectURL(secondaryImageUrl.value)
        secondaryImageUrl.value = null
    }
}

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
        (projectId: string, m: NonNullable<CalibScanStatusDto['latestMetrics']>) => {
            if (projectId !== props.project.id || !m) {
                return
            }
            const current = status.value
            if (!current) {
                return
            }
            status.value = {
                ...current,
                latestMetrics: m,
                lastUpdatedAt: m.timestamp,
            }
        }
    )

    // 接收主/从相机原图 JPEG 二进制，更新对应槽位的 Blob URL
    hubConnection.on(
        'ReceiveCalibScanFrameAsync',
        (
            projectId: string,
            cameraRole: number,
            jpegBytes: Uint8Array,
            _roundIndex: number,
            _frameIndexInRound: number
        ) => {
            if (projectId !== props.project.id || !jpegBytes || jpegBytes.byteLength === 0) {
                return
            }
            if (cameraRole === CalibScanCameraRole.Main) {
                revokeUrl('main')
                mainImageUrl.value = bytesToImageUrl(jpegBytes)
            } else if (cameraRole === CalibScanCameraRole.Secondary) {
                revokeUrl('secondary')
                secondaryImageUrl.value = bytesToImageUrl(jpegBytes)
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

onMounted(async () => {
    await startHub()
    await refreshStatus(true)
})

onUnmounted(async () => {
    await stopHub()
    // 组件卸载时释放所有 Blob URL，避免内存泄漏
    revokeUrl('main')
    revokeUrl('secondary')
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
                        label="开始采集"
                        icon="pi pi-play"
                        :loading="loading"
                        :disabled="!canStart"
                        @click="onStart"
                    />
                    <Button
                        label="结束采集"
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

            <p v-if="status?.errorMessage" class="mt-3 text-sm text-red-400">
                {{ status.errorMessage }}
            </p>
            <p v-if="conflictHint" class="mt-2 text-sm text-amber-300">会话冲突：{{ conflictHint }}</p>
            <p class="mt-2 text-sm text-muted-foreground">
                投影仪按 Step3 周期参数循环播放条纹图，主/从相机软件触发同步抓拍；检测到十字图即本轮结束，自动进入下一轮采集。
            </p>
        </div>

        <div class="rounded-xl border border-border/60 bg-card/40 p-4">
            <h4 class="mb-3 text-sm font-semibold">采集状态</h4>
            <div class="grid grid-cols-2 gap-3 md:grid-cols-5">
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">当前轮次</div>
                    <div class="text-lg font-semibold">{{ metrics?.roundIndex ?? 0 }}</div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">轮内帧序号</div>
                    <div class="text-lg font-semibold">
                        {{ metrics?.frameIndexInRound ?? 0 }}
                        <span class="text-xs font-normal text-muted-foreground">
                            / {{ metrics?.patternCount ?? 0 }}
                        </span>
                    </div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">累计帧数</div>
                    <div class="text-lg font-semibold">{{ metrics?.frameIndex ?? 0 }}</div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">十字图检测</div>
                    <div class="text-lg font-semibold">
                        <Tag
                            :severity="metrics?.isCrosshairDetected ? 'success' : 'secondary'"
                            :value="metrics?.isCrosshairDetected ? '已检测' : '未检测'"
                        />
                    </div>
                </div>
                <div class="rounded-lg border border-border/50 p-3">
                    <div class="text-xs text-muted-foreground">最后更新时间</div>
                    <div class="text-xs font-medium pt-1 break-all">{{ status?.lastUpdatedAt ?? '-' }}</div>
                </div>
            </div>
        </div>

        <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div class="rounded-xl border border-border/60 bg-card/40 p-4">
                <div class="mb-3 flex items-center justify-between">
                    <h4 class="text-sm font-semibold">主相机原图</h4>
                    <Tag severity="info" value="Main" />
                </div>
                <div class="aspect-video w-full overflow-hidden rounded bg-black/70">
                    <img
                        v-if="mainImageUrl"
                        :src="mainImageUrl"
                        alt="main-camera-preview"
                        class="h-full w-full object-contain"
                    />
                    <div v-else class="flex h-full items-center justify-center text-xs text-muted-foreground">
                        暂无图像，请启动扫描并等待首帧推送
                    </div>
                </div>
            </div>

            <div class="rounded-xl border border-border/60 bg-card/40 p-4">
                <div class="mb-3 flex items-center justify-between">
                    <h4 class="text-sm font-semibold">从相机原图</h4>
                    <Tag severity="info" value="Secondary" />
                </div>
                <div class="aspect-video w-full overflow-hidden rounded bg-black/70">
                    <img
                        v-if="secondaryImageUrl"
                        :src="secondaryImageUrl"
                        alt="secondary-camera-preview"
                        class="h-full w-full object-contain"
                    />
                    <div v-else class="flex h-full items-center justify-center text-xs text-muted-foreground">
                        暂无图像，请启动扫描并等待首帧推送
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
