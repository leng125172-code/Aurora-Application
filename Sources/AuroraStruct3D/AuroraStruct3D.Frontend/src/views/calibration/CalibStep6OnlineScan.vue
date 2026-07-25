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
import PointCloudViewer from '@/components/PointCloudViewer.vue'
import { parsePlyHeader, parsePlyData } from '@/utils/ply-parser'

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

// 增量点云实时预览状态
const pointCloudData = ref<Float32Array | null>(null)
const pointCloudHasColor = ref(false)
const incrementalPointCount = ref(0)
const totalPointCount = ref(0)

let hubConnection: signalR.HubConnection | null = null
const previewControllers = new Map<number, AbortController>()

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

function indexOfBytes(source: Uint8Array, pattern: Uint8Array, start = 0): number {
    outer: for (let i = start; i <= source.length - pattern.length; i++) {
        for (let j = 0; j < pattern.length; j++) {
            if (source[i + j] !== pattern[j]) continue outer
        }
        return i
    }
    return -1
}

function appendBytes(
    left: Uint8Array<ArrayBufferLike>,
    right: Uint8Array<ArrayBufferLike>
): Uint8Array<ArrayBuffer> {
    const merged = new Uint8Array(left.length + right.length)
    merged.set(left)
    merged.set(right, left.length)
    return merged
}

/** 通过 HTTP Multipart BMP 流读取 Step6 主/从相机图像。 */
async function startPreviewStream(cameraRole: CalibScanCameraRole): Promise<void> {
    previewControllers.get(cameraRole)?.abort()
    const controller = new AbortController()
    previewControllers.set(cameraRole, controller)

    try {
        const response = await fetch(
            `/api/streaming/calibration/${props.project.id}/cameras/${cameraRole}/preview?t=${Date.now()}`,
            { signal: controller.signal, cache: 'no-store' }
        )
        if (!response.ok || !response.body) {
            throw new Error(`在线标定预览流连接失败：HTTP ${response.status}`)
        }

        const contentType = response.headers.get('Content-Type') ?? ''
        const boundaryMatch = /boundary\s*=\s*"?([^";]+)"?/i.exec(contentType)
        if (!boundaryMatch) throw new Error('在线标定预览流缺少 boundary')

        const encoder = new TextEncoder()
        const decoder = new TextDecoder('ascii')
        const separator = encoder.encode(`--${boundaryMatch[1]}\r\n`)
        const headerEnd = encoder.encode('\r\n\r\n')
        const frameMime = response.headers.get('X-Frame-Content-Type') ?? 'image/bmp'
        const reader = response.body.getReader()
        let buffer = new Uint8Array(0)

        while (true) {
            const { done, value } = await reader.read()
            if (done) break
            if (value?.length) buffer = appendBytes(buffer, value)

            while (true) {
                const separatorIndex = indexOfBytes(buffer, separator)
                if (separatorIndex < 0) break
                const headersStart = separatorIndex + separator.length
                const headersEnd = indexOfBytes(buffer, headerEnd, headersStart)
                if (headersEnd < 0) break

                const headers = decoder.decode(buffer.subarray(headersStart, headersEnd))
                const lengthMatch = /^Content-Length:\s*(\d+)\s*$/im.exec(headers)
                if (!lengthMatch) {
                    buffer = buffer.subarray(headersStart)
                    continue
                }

                const payloadStart = headersEnd + headerEnd.length
                const payloadEnd = payloadStart + Number(lengthMatch[1])
                if (buffer.length < payloadEnd) break

                const frame = buffer.slice(payloadStart, payloadEnd)
                const url = URL.createObjectURL(new Blob([frame], { type: frameMime }))
                if (cameraRole === CalibScanCameraRole.Main) {
                    revokeUrl('main')
                    mainImageUrl.value = url
                } else {
                    revokeUrl('secondary')
                    secondaryImageUrl.value = url
                }
                buffer = buffer.subarray(payloadEnd)
            }
        }
    } catch (e) {
        if ((e as { name?: string })?.name !== 'AbortError') {
            console.warn(`在线标定相机角色 ${cameraRole} 预览流异常`, e)
        }
    } finally {
        if (previewControllers.get(cameraRole) === controller) {
            previewControllers.delete(cameraRole)
        }
    }
}

function stopPreviewStreams(): void {
    for (const controller of previewControllers.values()) controller.abort()
    previewControllers.clear()
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

    hubConnection.on(
        'ReceiveIncrementalPointCloudAsync',
        (
            projectId: string,
            pointCloudBytes: Uint8Array,
            pointCount: number,
            totalPoint: number
        ) => {
            if (projectId !== props.project.id || !pointCloudBytes || pointCloudBytes.length === 0) {
                return
            }

            try {
                const header = parsePlyHeader(pointCloudBytes)
                const data = parsePlyData(pointCloudBytes, header)

                if (pointCloudData.value) {
                    const newLength = pointCloudData.value.length + data.length
                    const merged = new Float32Array(newLength)
                    merged.set(pointCloudData.value)
                    merged.set(data, pointCloudData.value.length)
                    pointCloudData.value = merged
                } else {
                    pointCloudData.value = data
                }

                pointCloudHasColor.value = header.hasColor
                incrementalPointCount.value = pointCount
                totalPointCount.value = totalPoint
            } catch (e) {
                console.error('解析增量点云失败:', e)
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
    void startPreviewStream(CalibScanCameraRole.Main)
    void startPreviewStream(CalibScanCameraRole.Secondary)
})

onUnmounted(async () => {
    stopPreviewStreams()
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

        <div class="rounded-xl border border-border/60 bg-card/40 p-4">
            <div class="mb-3 flex items-center justify-between">
                <h4 class="text-sm font-semibold">点云实时预览</h4>
                <div class="flex items-center gap-2">
                    <Tag
                        :severity="pointCloudData ? 'success' : 'secondary'"
                        :value="pointCloudData ? '已生成' : '等待中'"
                    />
                    <span class="text-xs text-muted-foreground">
                        新增 {{ incrementalPointCount }} 点 / 累计 {{ totalPointCount.toLocaleString() }} 点
                    </span>
                </div>
            </div>
            <div class="h-80 w-full overflow-hidden rounded bg-black/70">
                <PointCloudViewer
                    v-if="pointCloudData"
                    :point-data="pointCloudData"
                    :has-color="pointCloudHasColor"
                    class="h-full w-full"
                />
                <div v-else class="flex h-full items-center justify-center text-xs text-muted-foreground">
                    暂无点云数据，启动扫描后将实时生成点云预览
                </div>
            </div>
        </div>
    </div>
</template>
