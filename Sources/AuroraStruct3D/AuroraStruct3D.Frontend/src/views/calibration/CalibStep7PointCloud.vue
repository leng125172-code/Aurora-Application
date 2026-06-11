<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import Button from 'primevue/button'
import ProgressBar from 'primevue/progressbar'
import Tag from 'primevue/tag'
import { Download, FileBox, RefreshCw, X } from '@lucide/vue'
import { showErrorToastOnce } from '@/api/client'
import { useAppToast } from '@/composables/useAppToast'
import type { CalibProjectDto } from '@/api/calibration'
import {
    PointCloudRunState,
    cancelPointCloud,
    generatePointCloud,
    getPointCloudStatus,
    type PointCloudStatusDto,
} from '@/api/calib-point-cloud'

const props = defineProps<{
    project: CalibProjectDto
}>()

const { success: toastSuccess } = useAppToast()

const loading = ref(false)
const status = ref<PointCloudStatusDto | null>(null)
const hubConnected = ref(false)
let hubConnection: signalR.HubConnection | null = null

// ===================== 计算属性 =====================

const stateText = computed(() => {
    switch (status.value?.state) {
        case PointCloudRunState.Running:
            return '生成中'
        case PointCloudRunState.Completed:
            return '已完成'
        case PointCloudRunState.Failed:
            return '生成失败'
        default:
            return '就绪'
    }
})

const stateSeverity = computed<'success' | 'info' | 'warn' | 'danger' | 'secondary'>(() => {
    switch (status.value?.state) {
        case PointCloudRunState.Running:
            return 'info'
        case PointCloudRunState.Completed:
            return 'success'
        case PointCloudRunState.Failed:
            return 'danger'
        default:
            return 'secondary'
    }
})

const canGenerate = computed(() => !loading.value && !status.value?.isRunning)

const canCancel = computed(() => !loading.value && !!status.value?.isRunning)

const fileSizeLabel = computed(() => {
    const bytes = status.value?.plyFileSizeBytes
    if (!bytes) return null
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / 1024 / 1024).toFixed(2)} MB`
})

// ===================== SignalR =====================

async function startHub(): Promise<void> {
    if (hubConnection?.state === signalR.HubConnectionState.Connected) return
    if (hubConnection && hubConnection.state !== signalR.HubConnectionState.Disconnected) return

    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .configureLogging(signalR.LogLevel.Warning)
        .build()

    hubConnection.onreconnected(async () => {
        hubConnected.value = true
        try {
            await hubConnection?.invoke('JoinPointCloudGroupAsync', props.project.id)
        } catch {
            /* 忽略 */
        }
        void refreshStatus(false)
    })

    hubConnection.onclose(() => {
        hubConnected.value = false
    })

    hubConnection.on('ReceivePointCloudStatusAsync', (next: PointCloudStatusDto) => {
        if (!next || next.calibProjectId !== props.project.id) return
        status.value = next
    })

    try {
        await hubConnection.start()
        hubConnected.value = true
        await hubConnection.invoke('JoinPointCloudGroupAsync', props.project.id)
    } catch (e) {
        hubConnected.value = false
        showErrorToastOnce(e)
    }
}

async function stopHub(): Promise<void> {
    if (!hubConnection) return
    try {
        await hubConnection.invoke('LeavePointCloudGroupAsync', props.project.id)
        await hubConnection.stop()
    } finally {
        hubConnection = null
        hubConnected.value = false
    }
}

// ===================== 数据操作 =====================

async function refreshStatus(showLoading = true): Promise<void> {
    if (showLoading) loading.value = true
    try {
        status.value = await getPointCloudStatus(props.project.id)
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        if (showLoading) loading.value = false
    }
}

async function onGenerate(): Promise<void> {
    loading.value = true
    try {
        status.value = await generatePointCloud({ calibProjectId: props.project.id })
        toastSuccess('已开始生成点云')
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        loading.value = false
    }
}

async function onCancel(): Promise<void> {
    loading.value = true
    try {
        status.value = await cancelPointCloud(props.project.id)
        toastSuccess('已取消点云生成')
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        loading.value = false
    }
}

function onDownload(): void {
    const url = status.value?.plyDownloadUrl
    if (!url) return
    const a = document.createElement('a')
    a.href = url
    a.download = `point-cloud-${props.project.id}.ply`
    a.click()
}

// ===================== 生命周期 =====================

onMounted(async () => {
    await startHub()
    await refreshStatus()
})

onUnmounted(async () => {
    await stopHub()
})
</script>

<template>
    <div class="flex flex-1 min-h-0 flex-col gap-4 overflow-y-auto p-4">
        <!-- 标题栏 -->
        <div class="flex items-center justify-between gap-3">
            <div class="flex items-center gap-2">
                <FileBox class="size-5 text-primary" />
                <h2 class="text-lg font-semibold">点云生成</h2>
            </div>
            <div class="flex items-center gap-2">
                <Tag :severity="stateSeverity" :value="stateText" />
                <Button text rounded severity="secondary" size="small" :loading="loading" @click="refreshStatus()">
                    <RefreshCw class="size-4" />
                </Button>
            </div>
        </div>

        <!-- 说明卡片 -->
        <div class="rounded-lg border border-border/40 bg-card/60 p-4 text-sm text-muted-foreground">
            <p>根据项目配置自动触发结构光采集并生成 PLY 格式点云文件。生成过程约需数十秒，完成后可下载查看。</p>
        </div>

        <!-- 进度区 -->
        <div
            v-if="
                status?.isRunning ||
                status?.state === PointCloudRunState.Completed ||
                status?.state === PointCloudRunState.Failed
            "
            class="rounded-lg border border-border/40 bg-card/60 p-4 flex flex-col gap-3"
        >
            <!-- 进度条（生成中） -->
            <template v-if="status?.isRunning">
                <div class="flex items-center justify-between text-sm">
                    <span class="text-muted-foreground">{{ status.progressMessage ?? '处理中...' }}</span>
                    <span class="font-medium">{{ status.progress }}%</span>
                </div>
                <ProgressBar :value="status.progress" class="h-2" />
            </template>

            <!-- 完成 -->
            <template v-else-if="status?.state === PointCloudRunState.Completed">
                <div class="flex flex-col gap-2">
                    <p class="text-sm text-green-400 font-medium">✓ 点云生成完成</p>
                    <div class="flex items-center gap-3">
                        <span v-if="fileSizeLabel" class="text-xs text-muted-foreground">
                            文件大小：{{ fileSizeLabel }}
                        </span>
                        <Button size="small" @click="onDownload">
                            <Download class="size-4 mr-1" />
                            下载 PLY 文件
                        </Button>
                    </div>
                </div>
            </template>

            <!-- 失败 -->
            <template v-else-if="status?.state === PointCloudRunState.Failed">
                <p class="text-sm text-red-400">✕ 生成失败：{{ status.errorMessage ?? '未知错误' }}</p>
            </template>
        </div>

        <!-- 操作按钮 -->
        <div class="flex items-center gap-3">
            <Button :disabled="!canGenerate" :loading="loading && !status?.isRunning" @click="onGenerate">
                <FileBox class="size-4 mr-1" />
                {{ status?.state === PointCloudRunState.Completed ? '重新生成' : '生成点云' }}
            </Button>
            <Button v-if="canCancel" severity="secondary" outlined :loading="loading" @click="onCancel">
                <X class="size-4 mr-1" />
                取消
            </Button>
        </div>
    </div>
</template>
