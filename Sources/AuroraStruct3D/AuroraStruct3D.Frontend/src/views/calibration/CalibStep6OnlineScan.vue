<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { showErrorToastOnce } from '@/api/client'
import { useAppToast } from '@/composables/useAppToast'
import { CalibDeviceType, type CalibProjectDto } from '@/api/calibration'
import {
  CalibScanMode,
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
const selectedMode = ref<CalibScanMode | null>(null)
const previewUrls = ref<Record<string, string>>({})
const hubConnected = ref(false)
const reconnecting = ref(false)
const conflictHint = ref<string | null>(null)
let hubConnection: signalR.HubConnection | null = null

const modeOptions = computed(() => {
  const options = [
    { label: '双目无光', value: CalibScanMode.TwoCamera0Light },
    { label: '单目一光', value: CalibScanMode.OneCamera1Light },
    { label: '双目一光', value: CalibScanMode.TwoCamera1Light },
  ]

  if (props.project.deviceType === CalibDeviceType.TwoCamera0Light) {
    return options.filter((x) => x.value === CalibScanMode.TwoCamera0Light)
  }

  if (props.project.deviceType === CalibDeviceType.OneCamera1Light) {
    return options.filter((x) => x.value === CalibScanMode.OneCamera1Light)
  }

  return options.filter((x) => x.value === CalibScanMode.TwoCamera1Light)
})

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
  return !loading.value && !status.value?.isRunning && selectedMode.value != null
})

const canStop = computed(() => {
  return !loading.value && !!status.value?.isRunning
})

const mainCameraPreviewUrl = computed(() => {
  const id = props.project.mainCameraDeviceId
  return id ? (previewUrls.value[id] ?? null) : null
})

const secondaryCameraPreviewUrl = computed(() => {
  const id = props.project.secondaryCameraDeviceId
  return id ? (previewUrls.value[id] ?? null) : null
})

function normalizeFrameBytes(frame: Uint8Array | ArrayBuffer | number[]): Uint8Array {
  if (frame instanceof Uint8Array) {
    return frame
  }
  if (frame instanceof ArrayBuffer) {
    return new Uint8Array(frame)
  }
  if (Array.isArray(frame)) {
    return Uint8Array.from(frame)
  }
  return new Uint8Array()
}

function updatePreviewFrame(cameraId: string, frame: Uint8Array | ArrayBuffer | number[]): void {
  const bytes = normalizeFrameBytes(frame)
  if (bytes.byteLength === 0) {
    return
  }

  const oldUrl = previewUrls.value[cameraId]
  const safeBytes = Uint8Array.from(bytes)
  const blob = new Blob([safeBytes.buffer], {
    type: 'image/jpeg',
  })
  const url = URL.createObjectURL(blob)
  previewUrls.value = {
    ...previewUrls.value,
    [cameraId]: url,
  }

  if (oldUrl) {
    window.setTimeout(() => URL.revokeObjectURL(oldUrl), 500)
  }
}

function releasePreviewUrls(): void {
  Object.values(previewUrls.value).forEach((url) => URL.revokeObjectURL(url))
  previewUrls.value = {}
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
    if (selectedMode.value == null) {
      selectedMode.value = next.scanMode
    }
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
    },
  )

  hubConnection.on('ReceiveCameraFrameAsync', (cameraId: string, frame: Uint8Array | ArrayBuffer | number[]) => {
    const mainId = props.project.mainCameraDeviceId
    const secondaryId = props.project.secondaryCameraDeviceId
    if (cameraId === mainId || cameraId === secondaryId) {
      updatePreviewFrame(cameraId, frame)
    }
  })

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
    if (selectedMode.value == null) {
      selectedMode.value = res.scanMode
    }
  } catch (e) {
    showErrorToastOnce(e)
  } finally {
    if (showLoading) {
      loading.value = false
    }
  }
}

async function onStart(): Promise<void> {
  if (!selectedMode.value) {
    return
  }

  loading.value = true
  conflictHint.value = null
  try {
    const res = await startCalibScan({
      calibProjectId: props.project.id,
      scanMode: selectedMode.value,
    })
    status.value = res
    toastSuccess('已启动在线扫描会话')
  } catch (e) {
    const code = (e as { response?: { data?: { error?: { code?: string } } } })?.response?.data?.error?.code
    const message = (e as { response?: { data?: { error?: { message?: string } } } })?.response?.data?.error?.message
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
  selectedMode.value = modeOptions.value[0]?.value ?? null
  await startHub()
  await refreshStatus(true)
})

onUnmounted(async () => {
  await stopHub()
  releasePreviewUrls()
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
        <div class="space-y-1 md:col-span-1">
          <label class="text-xs text-muted-foreground">扫描模式</label>
          <Select
            v-model="selectedMode"
            :options="modeOptions"
            option-label="label"
            option-value="value"
            class="w-full"
            :disabled="status?.isRunning"
          />
        </div>

        <div class="md:col-span-2 flex items-end gap-2">
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

      <p v-if="status?.errorMessage" class="mt-3 text-sm text-red-400">
        {{ status.errorMessage }}
      </p>
      <p v-if="conflictHint" class="mt-2 text-sm text-amber-300">
        会话冲突：{{ conflictHint }}
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
          <div class="text-lg font-semibold">{{ ((status?.latestMetrics?.depthValidRate ?? 0) * 100).toFixed(2) }}%</div>
        </div>
        <div class="rounded-lg border border-border/50 p-3">
          <div class="text-xs text-muted-foreground">置信度</div>
          <div class="text-lg font-semibold">{{ ((status?.latestMetrics?.confidence ?? 0) * 100).toFixed(2) }}%</div>
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
      <h4 class="mb-3 text-sm font-semibold">实时预览</h4>
      <div class="grid grid-cols-1 gap-3 md:grid-cols-2">
        <div class="rounded-lg border border-border/50 p-2">
          <div class="mb-2 text-xs text-muted-foreground">主相机</div>
          <div class="aspect-video w-full overflow-hidden rounded bg-black/60">
            <img
              v-if="mainCameraPreviewUrl"
              :src="mainCameraPreviewUrl"
              alt="main-camera-preview"
              class="h-full w-full object-contain"
            />
            <div v-else class="flex h-full items-center justify-center text-xs text-muted-foreground">
              暂无预览帧
            </div>
          </div>
        </div>
        <div class="rounded-lg border border-border/50 p-2">
          <div class="mb-2 text-xs text-muted-foreground">从相机</div>
          <div class="aspect-video w-full overflow-hidden rounded bg-black/60">
            <img
              v-if="secondaryCameraPreviewUrl"
              :src="secondaryCameraPreviewUrl"
              alt="secondary-camera-preview"
              class="h-full w-full object-contain"
            />
            <div v-else class="flex h-full items-center justify-center text-xs text-muted-foreground">
              暂无预览帧
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
