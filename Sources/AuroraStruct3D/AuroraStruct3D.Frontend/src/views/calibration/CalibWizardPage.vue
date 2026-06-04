<script setup lang="ts">
/**
 * 标定向导页
 * 使用 PrimeVue Stepper 引导用户完成多步标定流程
 * Step 1：设备标定信息（左侧项目信息 + 右侧设备布局SVG图）
 * Step 2：相机参数配置（列出所有相机，可展开设置各相机镜头/传感器参数）
 */
import { ref, computed, onMounted, watch, nextTick, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Stepper from 'primevue/stepper'
import StepList from 'primevue/steplist'
import Step from 'primevue/step'
import StepPanels from 'primevue/steppanels'
import StepPanel from 'primevue/steppanel'
import BorderBeam from '@/components/ui/border-beam/BorderBeam.vue'
import { ArrowLeft, ChevronDown, ChevronRight, Camera, AlertCircle, RefreshCcw, Save, Loader2 } from '@lucide/vue'
import { showErrorToastOnce } from '@/api/client'
import { useAppToast } from '@/composables/useAppToast'
import {
    CalibDeviceType,
    DeviceSeries,
    getCalibProjectAsync,
    type CalibProjectDto,
    CMOS_SENSOR_SIZES,
    type CmosSensorSize,
    saveCalibCameraParamAsync,
    getCalibCameraParamListAsync,
} from '@/api/calibration'
import {
    getCameraList,
    openCamera,
    stopPreview,
    batchGetGenICamParams,
    CameraStatus,
    type CameraDeviceDto,
} from '@/api/cameras'
import * as signalR from '@microsoft/signalr'
import {
    getProjectorList,
    type ProjectorDeviceDto,
    getProjectorPixelResolution,
    generateFringePreview,
    getProjectorFringeDownloadStatus,
    type ProjectorFringeDownloadStatusDto,
    downloadFringePattern,
} from '@/api/projectors'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

// ===================== 数据加载 =====================

const loading = ref(false)
const project = ref<CalibProjectDto | null>(null)

async function loadProject(): Promise<void> {
    const id = route.params['id'] as string
    if (!id) return
    loading.value = true
    try {
        project.value = await getCalibProjectAsync(id)
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        loading.value = false
    }
}

onMounted(() => loadProject())

// ===================== 辅助函数 =====================

/** 设备系列标签 */
function deviceSeriesLabel(series: DeviceSeries): string {
    return series === DeviceSeries.NoLight ? t('calib.seriesNoLight') : t('calib.seriesSingleLight')
}

/** 设备类型标签 */
function deviceTypeLabel(type: CalibDeviceType): string {
    const map: Record<CalibDeviceType, string> = {
        [CalibDeviceType.TwoCamera0Light]: t('calib.type2C0L'),
        [CalibDeviceType.ThreeCamera0Light]: t('calib.type3C0L'),
        [CalibDeviceType.OneCamera1Light]: t('calib.type1C1L'),
        [CalibDeviceType.TwoCamera1Light]: t('calib.type2C1L'),
        [CalibDeviceType.ThreeCamera1Light]: t('calib.type3C1L'),
    }
    return map[type] ?? String(type)
}

// ===================== Stepper 状态 =====================

const activeStep = ref('1')

// ===================== 步骤列表 =====================

const steps = computed(() => [
    { value: '1', label: t('calib.step1Label') },
    { value: '2', label: t('calib.step2Label') },
    { value: '3', label: t('calib.step3Label') },
    { value: '4', label: t('calib.step4Label') },
    { value: '5', label: t('calib.step5Label') },
    { value: '6', label: t('calib.step6Label') },
    { value: '7', label: t('calib.step7Label') },
])

function goBack(): void {
    void router.push({ name: 'CalibProjectManage' })
}

// ===================== Step 2 相机参数配置 =====================

const { success: toastSuccess } = useAppToast()

/** 单个相机的表单数据（字段名与 SaveCalibCameraParamInput 一致） */
interface CameraParamForm {
    sensorSize: string | null
    lensFocalLength: number | null
    maxAperture: number | null
    minAperture: number | null
    currentAperture: number | null
}

/** 从相机硬件读取的只读数据（字段名与 SaveCalibCameraParamInput 一致） */
interface CameraHardwareData {
    imageWidthPixels: number | null
    imageHeightPixels: number | null
    exposureTimeMinUs: number | null
    exposureTimeMaxUs: number | null
}

const step2Cameras = ref<CameraDeviceDto[]>([])
const step2Loading = ref(false)
const expandedCameraId = ref<string | null>(null)
const cameraConnecting = ref<Record<string, boolean>>({})
const cameraForms = ref<Record<string, CameraParamForm>>({})
const cameraHardware = ref<Record<string, CameraHardwareData | null>>({})
const hardwareLoading = ref<Record<string, boolean>>({})
const cameraSaving = ref<Record<string, boolean>>({})

/** 根据 code 找到 CMOS 尺寸对象 */
function getSelectedCmosSize(code: string | null): CmosSensorSize | null {
    if (!code) return null
    return CMOS_SENSOR_SIZES.find((s) => s.code === code) ?? null
}

/** 计算像素尺寸 (μm/像素) */
function calcPixelSize(cameraId: string): number | null {
    const form = cameraForms.value[cameraId]
    const hw = cameraHardware.value[cameraId]
    if (!form || !hw?.imageWidthPixels || !hw?.imageHeightPixels) return null
    const size = getSelectedCmosSize(form.sensorSize)
    if (!size) return null
    const sensorDiagonalUm = Math.hypot(size.widthMm, size.heightMm) * 1000
    const imageDiagonalPixels = Math.hypot(hw.imageWidthPixels, hw.imageHeightPixels)
    if (imageDiagonalPixels <= 0) return null
    return parseFloat((sensorDiagonalUm / imageDiagonalPixels).toFixed(3))
}

/** 初始化相机表单（确保对象存在） */
function initCameraForm(cameraId: string): void {
    if (!cameraForms.value[cameraId]) {
        cameraForms.value[cameraId] = {
            sensorSize: null,
            lensFocalLength: null,
            maxAperture: null,
            minAperture: null,
            currentAperture: null,
        }
    }
}

/** 相机状态颜色映射 */
function cameraStatusColor(status: CameraStatus): string {
    switch (status) {
        case CameraStatus.Ready:
            return 'text-green-400'
        case CameraStatus.Capturing:
            return 'text-blue-400'
        case CameraStatus.Error:
            return 'text-red-400'
        case CameraStatus.Closed:
            return 'text-muted-foreground'
        default:
            return 'text-muted-foreground'
    }
}

/** 相机状态标签 */
function cameraStatusLabel(status: CameraStatus): string {
    switch (status) {
        case CameraStatus.Ready:
            return t('calib.step2Ready')
        case CameraStatus.Capturing:
            return t('calib.step2Capturing')
        case CameraStatus.Error:
            return t('calib.step2Error')
        case CameraStatus.Closed:
            return t('calib.step2Closed')
        default:
            return t('calib.step2Unknown')
    }
}

/** 进入 Step 2 时初始化：加载相机列表 + 加载已保存参数 + 自动处理连接状态 */
async function initStep2(): Promise<void> {
    step2Loading.value = true
    step2Cameras.value = []
    try {
        const result = await getCameraList({ maxResultCount: 200 })
        step2Cameras.value = result.items

        // 加载已保存的标定相机参数
        if (project.value) {
            try {
                const savedParams = await getCalibCameraParamListAsync(project.value.id)
                for (const param of savedParams) {
                    cameraForms.value[param.cameraDeviceId] = {
                        sensorSize: param.sensorSize ?? null,
                        lensFocalLength: param.lensFocalLength ?? null,
                        maxAperture: param.maxAperture ?? null,
                        minAperture: param.minAperture ?? null,
                        currentAperture: param.currentAperture ?? null,
                    }
                    if (param.imageWidthPixels || param.exposureTimeMinUs) {
                        cameraHardware.value[param.cameraDeviceId] = {
                            imageWidthPixels: param.imageWidthPixels ?? null,
                            imageHeightPixels: param.imageHeightPixels ?? null,
                            exposureTimeMinUs: param.exposureTimeMinUs ?? null,
                            exposureTimeMaxUs: param.exposureTimeMaxUs ?? null,
                        }
                    }
                }
            } catch {
                /* 允许无历史参数 */
            }
        }

        // 对每个启用的相机自动处理连接状态（并行处理）
        const tasks = result.items
            .filter((cam) => cam.isEnabled)
            .map(async (cam) => {
                initCameraForm(cam.id)
                if (cam.status === CameraStatus.Capturing) {
                    cameraConnecting.value[cam.id] = true
                    try {
                        await stopPreview(cam.id)
                    } catch {
                        /* 忽略 */
                    }
                    cameraConnecting.value[cam.id] = false
                } else if (cam.status === CameraStatus.Closed || cam.status === CameraStatus.Unknown) {
                    cameraConnecting.value[cam.id] = true
                    try {
                        await openCamera(cam.id)
                    } catch {
                        /* 忽略 */
                    }
                    cameraConnecting.value[cam.id] = false
                }
            })
        await Promise.all(tasks)
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        step2Loading.value = false
    }
}

/** 折叠/展开相机项 */
function toggleCameraExpand(cameraId: string): void {
    if (expandedCameraId.value === cameraId) {
        expandedCameraId.value = null
    } else {
        expandedCameraId.value = cameraId
        initCameraForm(cameraId)
    }
}

/** 从相机 GenICam 节点读取图像分辨率与曝光范围 */
async function readHardwareParams(cam: CameraDeviceDto): Promise<void> {
    if (hardwareLoading.value[cam.id]) return
    hardwareLoading.value[cam.id] = true
    try {
        const batch = await batchGetGenICamParams(cam.id, [
            { nodeName: 'Width', dataType: 'int' },
            { nodeName: 'Height', dataType: 'int' },
            { nodeName: 'ExposureAuto', dataType: 'int' },
            { nodeName: 'ExposureAutoMinTime', dataType: 'int' },
            { nodeName: 'ExposureAutoMaxTime', dataType: 'int' },
        ])
        const getStr = (name: string): string | null => {
            const r = batch.results.find((x) => x.nodeName === name)
            return r?.success && r.value != null ? r.value : null
        }
        const getNum = (name: string): number | null => {
            const v = getStr(name)
            return v != null ? parseFloat(v) : null
        }
        const w = getNum('Width')
        const h = getNum('Height')
        const exposureAuto = getNum('ExposureAuto')
        const expMin = getNum('ExposureAutoMinTime')
        const expMax = getNum('ExposureAutoMaxTime')
        const useExposureRange = exposureAuto != null && exposureAuto !== 0
        cameraHardware.value[cam.id] = {
            imageWidthPixels: w != null ? Math.round(w) : null,
            imageHeightPixels: h != null ? Math.round(h) : null,
            exposureTimeMinUs: useExposureRange && expMin != null ? Math.round(expMin) : 0,
            exposureTimeMaxUs: useExposureRange && expMax != null ? Math.round(expMax) : 0,
        }
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        hardwareLoading.value[cam.id] = false
    }
}

/** 保存单个相机的标定参数 */
async function saveCameraParams(cam: CameraDeviceDto): Promise<void> {
    if (cameraSaving.value[cam.id] || !project.value) return
    cameraSaving.value[cam.id] = true
    const form = cameraForms.value[cam.id]
    const hw = cameraHardware.value[cam.id]
    const cmos = getSelectedCmosSize(form?.sensorSize ?? null)
    try {
        await saveCalibCameraParamAsync({
            calibProjectId: project.value.id,
            cameraDeviceId: cam.id,
            name: cam.name,
            description: cam.description ?? null,
            isEnabled: cam.isEnabled,
            sensorSize: form?.sensorSize ?? null,
            sensorWidthMm: cmos?.widthMm ?? null,
            sensorHeightMm: cmos?.heightMm ?? null,
            imageWidthPixels: hw?.imageWidthPixels ?? null,
            imageHeightPixels: hw?.imageHeightPixels ?? null,
            lensFocalLength: form?.lensFocalLength ?? null,
            maxAperture: form?.maxAperture ?? null,
            minAperture: form?.minAperture ?? null,
            currentAperture: form?.currentAperture ?? null,
            exposureTimeMinUs: hw?.exposureTimeMinUs ?? null,
            exposureTimeMaxUs: hw?.exposureTimeMaxUs ?? null,
        })
        toastSuccess(t('calib.step2SaveSuccess'))
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        cameraSaving.value[cam.id] = false
    }
}

// ===================== Step 3 投影机参数 =====================

const step3Loading = ref(false)
const step3Projectors = ref<ProjectorDeviceDto[]>([])
const selectedProjectorId = ref<string | null>(null)
const projectorWidthPixels = ref<number | null>(null)
const projectorPixelMode = ref<string | null>(null)
const projectorReading = ref(false)

/** 条纹方向：horizontal=横条纹 vertical=竖条纹 */
const fringeMode = ref<'horizontal' | 'vertical'>('horizontal')
/** 条纹类型：bw=黑白（首色黑）wb=白黑（首色白） */
const fringeType = ref<'bw' | 'wb'>('bw')
const projectorHeightInput = ref<number>(1024)
const fringe3PeriodCount = ref<number>(8)
const fringe3ImageCount = ref<number>(4)
const fringe3PhaseShift = ref<number>(2)

interface FringeImageData {
    index: number
    label: string
    pixels: Uint8Array
}
const generatedFringeImages = ref<FringeImageData[]>([])
const selectedFringeImageIdx = ref<number>(0)
const generatingFringe = ref(false)
const downloadingFringe = ref(false)
const fringeDownloadProgress = ref<number>(0)
const previewCanvasRef = ref<HTMLCanvasElement | null>(null)

let step3Hub: signalR.HubConnection | null = null

function applyFringeDownloadStatus(status: ProjectorFringeDownloadStatusDto): void {
    if (status.projectorId !== selectedProjectorId.value) return
    fringeDownloadProgress.value = Math.max(0, Math.min(100, Math.round(status.progress ?? 0)))
    downloadingFringe.value = status.status === 'Running'
    if (status.status === 'Failed' && status.errorMessage) {
        showErrorToastOnce(status.errorMessage)
    }
}

async function refreshCurrentFringeDownloadStatus(): Promise<void> {
    if (!selectedProjectorId.value) return
    try {
        const status = await getProjectorFringeDownloadStatus(selectedProjectorId.value)
        applyFringeDownloadStatus(status)
    } catch {
        /* 状态查询失败时不阻断页面 */
    }
}

/** 当前条纹的有效像素数（竖条纹=宽，横条纹=高） */
const fringe3PixelCount = computed(() =>
    fringeMode.value === 'vertical' ? projectorWidthPixels.value : projectorHeightInput.value
)

/** 周期数整除校验 */
const fringe3PeriodError = computed<string | null>(() => {
    const px = fringe3PixelCount.value
    if (!px || px <= 0 || fringe3PeriodCount.value <= 0) return null
    return px % fringe3PeriodCount.value !== 0 ? t('calib.step3PeriodError') : null
})

/** 相移合法性校验 */
const fringe3PhaseError = computed<string | null>(() => {
    const ps = fringe3PhaseShift.value
    const pc = fringe3PeriodCount.value
    if (!Number.isInteger(ps) || ps <= 0 || ps >= pc) return t('calib.step3PhaseError')
    return null
})

/** 是否可生成图像 */
const fringe3CanGenerate = computed(() => {
    if (fringe3PeriodError.value || fringe3PhaseError.value) return false
    if (fringe3ImageCount.value <= 0) return false
    if (fringeMode.value === 'vertical') return projectorWidthPixels.value != null
    return projectorHeightInput.value > 0
})

async function initStep3Hub(): Promise<void> {
    step3Hub = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/projector', { skipNegotiation: false })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()
    step3Hub.on('ReceiveFringeDownloadStatusChangedAsync', (status: ProjectorFringeDownloadStatusDto) => {
        applyFringeDownloadStatus(status)
    })
    step3Hub.on('ReceiveFringeDownloadProgressAsync', (projectorId: string, progress: number) => {
        if (projectorId === selectedProjectorId.value) {
            fringeDownloadProgress.value = Math.round(progress)
            downloadingFringe.value = progress < 100
        }
    })
    step3Hub.onreconnected(() => {
        void refreshCurrentFringeDownloadStatus()
    })
    try {
        await step3Hub.start()
        await refreshCurrentFringeDownloadStatus()
    } catch {
        /* 忽略连接失败，下载进度通知不可用时不影响主功能 */
    }
}

async function initStep3(): Promise<void> {
    if (step3Loading.value) return
    step3Loading.value = true
    try {
        if (!step3Hub) await initStep3Hub()
        const result = await getProjectorList({ maxResultCount: 100 })
        step3Projectors.value = result.items.filter((p) => p.isEnabled)
        if (step3Projectors.value.length > 0 && !selectedProjectorId.value) {
            selectedProjectorId.value = step3Projectors.value[0].id
        }
        await refreshCurrentFringeDownloadStatus()
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        step3Loading.value = false
    }
}

async function fetchProjectorResolution(): Promise<void> {
    if (!selectedProjectorId.value || projectorReading.value) return
    projectorReading.value = true
    try {
        const result = await getProjectorPixelResolution(selectedProjectorId.value)
        projectorWidthPixels.value = result.widthPixels
        projectorPixelMode.value = result.pixelMode
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        projectorReading.value = false
    }
}

function decodeBase64ToBytes(base64: string): Uint8Array {
    const binary = window.atob(base64)
    const bytes = new Uint8Array(binary.length)
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i)
    }
    return bytes
}

async function generateFringeImages(): Promise<void> {
    if (
        !selectedProjectorId.value ||
        !projectorWidthPixels.value ||
        fringe3PeriodError.value ||
        fringe3PhaseError.value
    ) {
        return
    }
    generatingFringe.value = true
    try {
        const images = await generateFringePreview({
            projectorId: selectedProjectorId.value,
            fringeMode: fringeMode.value,
            fringeType: fringeType.value,
            widthPixels: projectorWidthPixels.value,
            heightPixels: projectorHeightInput.value,
            periodCount: fringe3PeriodCount.value,
            imageCount: fringe3ImageCount.value,
            phaseShift: fringe3PhaseShift.value,
        })

        generatedFringeImages.value = images.map((img) => ({
            index: img.index,
            label: img.label,
            pixels: decodeBase64ToBytes(img.pixels),
        }))
        selectedFringeImageIdx.value = 0
        toastSuccess(t('calib.step3GenerateSuccess', { count: images.length }))
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        generatingFringe.value = false
    }
}

function renderFringePreview(): void {
    const canvas = previewCanvasRef.value
    if (!canvas) return
    const img = generatedFringeImages.value[selectedFringeImageIdx.value]
    const ctx = canvas.getContext('2d')
    if (!ctx || !img) {
        ctx?.clearRect(0, 0, canvas.width, canvas.height)
        return
    }
    const pixels = img.pixels
    const W = canvas.width
    const H = canvas.height
    // 使用 ImageData 批量写入，性能远优于逐像素 fillRect
    const imageData = ctx.createImageData(W, H)
    const data = imageData.data
    if (fringeMode.value === 'vertical') {
        // 竖条纹：pixels[x] 对应第 x 列的灰度，同列所有行相同
        for (let x = 0; x < W; x++) {
            const g = pixels[Math.min(x, pixels.length - 1)]
            for (let y = 0; y < H; y++) {
                const i = (y * W + x) * 4
                data[i] = g
                data[i + 1] = g
                data[i + 2] = g
                data[i + 3] = 255
            }
        }
    } else {
        // 横条纹：pixels[y] 对应第 y 行的灰度，同行所有列相同
        for (let y = 0; y < H; y++) {
            const g = pixels[Math.min(y, pixels.length - 1)]
            const rowBase = y * W * 4
            for (let x = 0; x < W; x++) {
                const i = rowBase + x * 4
                data[i] = g
                data[i + 1] = g
                data[i + 2] = g
                data[i + 3] = 255
            }
        }
    }
    ctx.putImageData(imageData, 0, 0)
}

async function triggerFringeDownload(): Promise<void> {
    if (!selectedProjectorId.value || !projectorWidthPixels.value || downloadingFringe.value) return
    downloadingFringe.value = true
    fringeDownloadProgress.value = 0
    try {
        await downloadFringePattern({
            projectorId: selectedProjectorId.value,
            fringeMode: fringeMode.value,
            fringeType: fringeType.value,
            widthPixels: projectorWidthPixels.value,
            heightPixels: projectorHeightInput.value,
            periodCount: fringe3PeriodCount.value,
            imageCount: fringe3ImageCount.value,
            phaseShift: fringe3PhaseShift.value,
        })
        await refreshCurrentFringeDownloadStatus()
    } catch (e) {
        downloadingFringe.value = false
        fringeDownloadProgress.value = 0
        showErrorToastOnce(e)
    }
}

watch([selectedFringeImageIdx, generatedFringeImages, fringeMode], () => {
    nextTick(() => renderFringePreview())
})

// 切换步骤时自动初始化对应步骤
watch(activeStep, (val: string) => {
    if (val === '2') void initStep2()
    if (val === '3') void initStep3()
})

watch(selectedProjectorId, () => {
    void refreshCurrentFringeDownloadStatus()
})

onUnmounted(() => {
    if (step3Hub) {
        void step3Hub.stop()
        step3Hub = null
    }
})
</script>

<template>
    <div class="flex h-full flex-col gap-4">
        <!-- 页面标题 -->
        <div class="flex items-center gap-3">
            <Button text severity="secondary" size="small" @click="goBack">
                <ArrowLeft class="size-4" />
            </Button>
            <h1 class="text-2xl font-bold tracking-tight">
                {{ project?.name ?? t('calib.calibrate') }}
            </h1>
        </div>

        <!-- 卡片外壳：直接用原生 div 复现 AppCard 样式，避免 PrimeVue Card 不撠满高度的问题 -->
        <div
            class="relative flex flex-col flex-1 min-h-0 overflow-hidden rounded-xl bg-card/40 backdrop-blur border border-border shadow-sm"
            style="clip-path: inset(0 round 0.75rem)"
        >
            <BorderBeam :size="120" :duration="10" />
            <Stepper v-model:value="activeStep" linear class="flex flex-col flex-1 min-h-0 px-4 pt-4">
                <StepList>
                    <Step v-for="step in steps" :key="step.value" :value="step.value">
                        {{ step.label }}
                    </Step>
                </StepList>

                <StepPanels class="flex flex-col flex-1 min-h-0" :pt="{ root: { class: '!bg-transparent' } }">
                    <!-- ============ Step 1 设备初始化 ============ -->
                    <StepPanel
                        value="1"
                        class="flex flex-col flex-1 min-h-0"
                        :pt="{ root: { class: '!bg-transparent' } }"
                    >
                        <div class="flex flex-col flex-1 min-h-0 overflow-y-auto py-6">
                            <div v-if="loading" class="flex items-center justify-center py-12 text-muted-foreground">
                                {{ t('common.loading') }}
                            </div>
                            <div v-else-if="project" class="flex flex-col flex-1 min-h-0">
                                <!-- 顶部标题行 + 单条分割线 -->
                                <div class="flex items-baseline gap-6 pb-2 border-b border-border/50">
                                    <span class="w-52 shrink-0 text-base font-semibold">
                                        {{ t('calib.projectInfo') }}
                                    </span>
                                    <span class="flex-1 text-base font-semibold">
                                        {{ t('calib.deviceLayout') }}
                                    </span>
                                </div>

                                <!-- 内容区 -->
                                <div class="flex gap-6 flex-1 pt-5">
                                    <!-- 左侧：项目信息 -->
                                    <div class="w-52 shrink-0 flex flex-col gap-3">
                                        <dl class="flex flex-col gap-3 text-sm">
                                            <!-- 项目名称 -->
                                            <div>
                                                <dt class="text-muted-foreground mb-0.5">
                                                    {{ t('calib.colName') }}
                                                </dt>
                                                <dd class="font-medium break-all">{{ project.name }}</dd>
                                            </div>
                                            <!-- 设备系列 -->
                                            <div>
                                                <dt class="text-muted-foreground mb-0.5">
                                                    {{ t('calib.colDeviceSeries') }}
                                                </dt>
                                                <dd class="font-medium">
                                                    {{ deviceSeriesLabel(project.deviceSeries) }}
                                                </dd>
                                            </div>
                                            <!-- 设备类型 -->
                                            <div>
                                                <dt class="text-muted-foreground mb-0.5">
                                                    {{ t('calib.colDeviceType') }}
                                                </dt>
                                                <dd class="font-medium">
                                                    {{ deviceTypeLabel(project.deviceType) }}
                                                </dd>
                                            </div>
                                            <!-- 相机数量 -->
                                            <div>
                                                <dt class="text-muted-foreground mb-0.5">
                                                    {{ t('calib.colCameraCount2') }}
                                                </dt>
                                                <dd class="font-medium">
                                                    {{ project.cameraCount }} {{ t('calib.cameraUnit') }}
                                                </dd>
                                            </div>
                                            <!-- 结构光数量 -->
                                            <div>
                                                <dt class="text-muted-foreground mb-0.5">
                                                    {{ t('calib.colProjectorCount') }}
                                                </dt>
                                                <dd class="font-medium">
                                                    {{ project.projectorCount }} {{ t('calib.projectorUnit') }}
                                                </dd>
                                            </div>
                                        </dl>
                                    </div>

                                    <!-- 右侧：设备布局SVG图 -->
                                    <div class="flex-1 flex items-center justify-center min-w-0 min-h-0 self-stretch">
                                        <!-- 2目0光：两相机左右水平排布 -->
                                        <svg
                                            v-if="project.deviceType === CalibDeviceType.TwoCamera0Light"
                                            viewBox="0 0 400 220"
                                            class="w-full max-h-72 object-contain"
                                            aria-label="2目0光设备布局"
                                        >
                                            <!-- 基线 -->
                                            <line
                                                x1="100"
                                                y1="110"
                                                x2="300"
                                                y2="110"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.4"
                                            />
                                            <!-- 主相机（左） -->
                                            <rect
                                                x="60"
                                                y="70"
                                                width="80"
                                                height="80"
                                                rx="8"
                                                fill="none"
                                                stroke="#60a5fa"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="100"
                                                cy="110"
                                                r="16"
                                                fill="#60a5fa"
                                                fill-opacity="0.2"
                                                stroke="#60a5fa"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="100" cy="110" r="6" fill="#60a5fa" />
                                            <text
                                                x="100"
                                                y="175"
                                                text-anchor="middle"
                                                font-size="12"
                                                fill="currentColor"
                                                opacity="0.8"
                                            >
                                                主相机（左）
                                            </text>
                                            <!-- 从相机（右） -->
                                            <rect
                                                x="260"
                                                y="70"
                                                width="80"
                                                height="80"
                                                rx="8"
                                                fill="none"
                                                stroke="#34d399"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="300"
                                                cy="110"
                                                r="16"
                                                fill="#34d399"
                                                fill-opacity="0.2"
                                                stroke="#34d399"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="300" cy="110" r="6" fill="#34d399" />
                                            <text
                                                x="300"
                                                y="175"
                                                text-anchor="middle"
                                                font-size="12"
                                                fill="currentColor"
                                                opacity="0.8"
                                            >
                                                从相机（右）
                                            </text>
                                            <!-- 基线标注 -->
                                            <text
                                                x="200"
                                                y="105"
                                                text-anchor="middle"
                                                font-size="10"
                                                fill="currentColor"
                                                opacity="0.5"
                                            >
                                                基线
                                            </text>
                                        </svg>

                                        <!-- 3目0光：三相机等腰三角形 -->
                                        <svg
                                            v-else-if="project.deviceType === CalibDeviceType.ThreeCamera0Light"
                                            viewBox="0 0 400 280"
                                            class="w-full max-h-72 object-contain"
                                            aria-label="3目0光设备布局"
                                        >
                                            <!-- 连线 -->
                                            <line
                                                x1="200"
                                                y1="70"
                                                x2="90"
                                                y2="195"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.35"
                                            />
                                            <line
                                                x1="200"
                                                y1="70"
                                                x2="310"
                                                y2="195"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.35"
                                            />
                                            <line
                                                x1="90"
                                                y1="195"
                                                x2="310"
                                                y2="195"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.35"
                                            />
                                            <!-- 主相机（中上） -->
                                            <rect
                                                x="160"
                                                y="38"
                                                width="80"
                                                height="64"
                                                rx="8"
                                                fill="none"
                                                stroke="#60a5fa"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="200"
                                                cy="70"
                                                r="14"
                                                fill="#60a5fa"
                                                fill-opacity="0.2"
                                                stroke="#60a5fa"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="200" cy="70" r="5" fill="#60a5fa" />
                                            <text
                                                x="200"
                                                y="22"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.85"
                                            >
                                                主相机（中上）
                                            </text>
                                            <!-- 左下相机 -->
                                            <rect
                                                x="48"
                                                y="165"
                                                width="80"
                                                height="64"
                                                rx="8"
                                                fill="none"
                                                stroke="#34d399"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="88"
                                                cy="197"
                                                r="14"
                                                fill="#34d399"
                                                fill-opacity="0.2"
                                                stroke="#34d399"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="88" cy="197" r="5" fill="#34d399" />
                                            <text
                                                x="88"
                                                y="248"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.85"
                                            >
                                                左下相机
                                            </text>
                                            <!-- 右下相机 -->
                                            <rect
                                                x="272"
                                                y="165"
                                                width="80"
                                                height="64"
                                                rx="8"
                                                fill="none"
                                                stroke="#f59e0b"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="312"
                                                cy="197"
                                                r="14"
                                                fill="#f59e0b"
                                                fill-opacity="0.2"
                                                stroke="#f59e0b"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="312" cy="197" r="5" fill="#f59e0b" />
                                            <text
                                                x="312"
                                                y="248"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.85"
                                            >
                                                右下相机
                                            </text>
                                        </svg>

                                        <!-- 1目1光：主相机 + 中心结构光 -->
                                        <svg
                                            v-else-if="project.deviceType === CalibDeviceType.OneCamera1Light"
                                            viewBox="0 0 400 220"
                                            class="w-full max-h-72 object-contain"
                                            aria-label="1目1光设备布局"
                                        >
                                            <!-- 结构光（中心） -->
                                            <polygon
                                                points="200,60 220,130 180,130"
                                                fill="#f59e0b"
                                                fill-opacity="0.25"
                                                stroke="#f59e0b"
                                                stroke-width="2"
                                            />
                                            <rect
                                                x="180"
                                                y="38"
                                                width="40"
                                                height="30"
                                                rx="5"
                                                fill="none"
                                                stroke="#f59e0b"
                                                stroke-width="2"
                                            />
                                            <text
                                                x="200"
                                                y="160"
                                                text-anchor="middle"
                                                font-size="12"
                                                fill="currentColor"
                                                opacity="0.8"
                                            >
                                                主结构光（中心）
                                            </text>
                                            <!-- 基线 -->
                                            <line
                                                x1="100"
                                                y1="80"
                                                x2="180"
                                                y2="80"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="5,4"
                                                opacity="0.4"
                                            />
                                            <!-- 主相机（旁侧） -->
                                            <rect
                                                x="36"
                                                y="50"
                                                width="80"
                                                height="72"
                                                rx="8"
                                                fill="none"
                                                stroke="#60a5fa"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="76"
                                                cy="86"
                                                r="16"
                                                fill="#60a5fa"
                                                fill-opacity="0.2"
                                                stroke="#60a5fa"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="76" cy="86" r="6" fill="#60a5fa" />
                                            <text
                                                x="76"
                                                y="145"
                                                text-anchor="middle"
                                                font-size="12"
                                                fill="currentColor"
                                                opacity="0.8"
                                            >
                                                主相机
                                            </text>
                                        </svg>

                                        <!-- 2目1光：两相机 + 中间结构光 -->
                                        <svg
                                            v-else-if="project.deviceType === CalibDeviceType.TwoCamera1Light"
                                            viewBox="0 0 400 220"
                                            class="w-full max-h-72 object-contain"
                                            aria-label="2目1光设备布局"
                                        >
                                            <!-- 基线 -->
                                            <line
                                                x1="80"
                                                y1="110"
                                                x2="320"
                                                y2="110"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.35"
                                            />
                                            <!-- 主相机（左） -->
                                            <rect
                                                x="36"
                                                y="70"
                                                width="80"
                                                height="80"
                                                rx="8"
                                                fill="none"
                                                stroke="#60a5fa"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="76"
                                                cy="110"
                                                r="15"
                                                fill="#60a5fa"
                                                fill-opacity="0.2"
                                                stroke="#60a5fa"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="76" cy="110" r="5.5" fill="#60a5fa" />
                                            <text
                                                x="76"
                                                y="175"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.8"
                                            >
                                                主相机（左）
                                            </text>
                                            <!-- 结构光（中心） -->
                                            <polygon
                                                points="200,72 222,128 178,128"
                                                fill="#f59e0b"
                                                fill-opacity="0.25"
                                                stroke="#f59e0b"
                                                stroke-width="2"
                                            />
                                            <rect
                                                x="178"
                                                y="50"
                                                width="44"
                                                height="30"
                                                rx="5"
                                                fill="none"
                                                stroke="#f59e0b"
                                                stroke-width="2"
                                            />
                                            <text
                                                x="200"
                                                y="155"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.8"
                                            >
                                                主结构光（中心）
                                            </text>
                                            <!-- 从相机（右） -->
                                            <rect
                                                x="284"
                                                y="70"
                                                width="80"
                                                height="80"
                                                rx="8"
                                                fill="none"
                                                stroke="#34d399"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="324"
                                                cy="110"
                                                r="15"
                                                fill="#34d399"
                                                fill-opacity="0.2"
                                                stroke="#34d399"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="324" cy="110" r="5.5" fill="#34d399" />
                                            <text
                                                x="324"
                                                y="175"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.8"
                                            >
                                                从相机（右）
                                            </text>
                                        </svg>

                                        <!-- 3目1光：三相机等腰三角形 + 中心结构光 -->
                                        <svg
                                            v-else-if="project.deviceType === CalibDeviceType.ThreeCamera1Light"
                                            viewBox="0 0 400 300"
                                            class="w-full max-h-72 object-contain"
                                            aria-label="3目1光设备布局"
                                        >
                                            <!-- 连线 -->
                                            <line
                                                x1="200"
                                                y1="75"
                                                x2="95"
                                                y2="205"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.3"
                                            />
                                            <line
                                                x1="200"
                                                y1="75"
                                                x2="305"
                                                y2="205"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.3"
                                            />
                                            <line
                                                x1="95"
                                                y1="205"
                                                x2="305"
                                                y2="205"
                                                stroke="currentColor"
                                                stroke-width="1.5"
                                                stroke-dasharray="6,4"
                                                opacity="0.3"
                                            />
                                            <!-- 主相机（中上） -->
                                            <rect
                                                x="160"
                                                y="44"
                                                width="80"
                                                height="64"
                                                rx="8"
                                                fill="none"
                                                stroke="#60a5fa"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="200"
                                                cy="76"
                                                r="14"
                                                fill="#60a5fa"
                                                fill-opacity="0.2"
                                                stroke="#60a5fa"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="200" cy="76" r="5" fill="#60a5fa" />
                                            <text
                                                x="200"
                                                y="28"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.85"
                                            >
                                                主相机（中上）
                                            </text>
                                            <!-- 左下从相机 -->
                                            <rect
                                                x="50"
                                                y="175"
                                                width="80"
                                                height="64"
                                                rx="8"
                                                fill="none"
                                                stroke="#34d399"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="90"
                                                cy="207"
                                                r="14"
                                                fill="#34d399"
                                                fill-opacity="0.2"
                                                stroke="#34d399"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="90" cy="207" r="5" fill="#34d399" />
                                            <text
                                                x="90"
                                                y="258"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.85"
                                            >
                                                左下从相机
                                            </text>
                                            <!-- 右下从相机 -->
                                            <rect
                                                x="270"
                                                y="175"
                                                width="80"
                                                height="64"
                                                rx="8"
                                                fill="none"
                                                stroke="#f59e0b"
                                                stroke-width="2"
                                            />
                                            <circle
                                                cx="310"
                                                cy="207"
                                                r="14"
                                                fill="#f59e0b"
                                                fill-opacity="0.2"
                                                stroke="#f59e0b"
                                                stroke-width="1.5"
                                            />
                                            <circle cx="310" cy="207" r="5" fill="#f59e0b" />
                                            <text
                                                x="310"
                                                y="258"
                                                text-anchor="middle"
                                                font-size="11"
                                                fill="currentColor"
                                                opacity="0.85"
                                            >
                                                右下从相机
                                            </text>
                                            <!-- 结构光（中心） -->
                                            <polygon
                                                points="200,148 212,180 188,180"
                                                fill="#a78bfa"
                                                fill-opacity="0.3"
                                                stroke="#a78bfa"
                                                stroke-width="1.5"
                                            />
                                            <rect
                                                x="188"
                                                y="134"
                                                width="24"
                                                height="18"
                                                rx="4"
                                                fill="none"
                                                stroke="#a78bfa"
                                                stroke-width="1.5"
                                            />
                                            <text
                                                x="200"
                                                y="196"
                                                text-anchor="middle"
                                                font-size="10"
                                                fill="#a78bfa"
                                                opacity="0.9"
                                            >
                                                结构光
                                            </text>
                                        </svg>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Step 1 底部导航 -->
                        <div class="flex justify-end gap-2 px-4 py-3 border-t border-border/40 mt-auto">
                            <Button size="small" :disabled="!project" @click="activeStep = '2'">
                                {{ t('calib.nextStep') }}
                            </Button>
                        </div>
                    </StepPanel>

                    <!-- ============ Step 2 相机参数配置 ============ -->
                    <StepPanel
                        value="2"
                        class="flex flex-col flex-1 min-h-0"
                        :pt="{ root: { class: '!bg-transparent' } }"
                    >
                        <div class="flex flex-col flex-1 min-h-0 overflow-y-auto py-6 px-2">
                            <!-- 加载中 -->
                            <div
                                v-if="step2Loading"
                                class="flex items-center justify-center gap-2 py-16 text-muted-foreground text-sm"
                            >
                                <Loader2 class="size-4 animate-spin" />
                                {{ t('common.loading') }}
                            </div>

                            <!-- 无相机 -->
                            <div
                                v-else-if="step2Cameras.length === 0"
                                class="flex flex-col items-center justify-center gap-3 py-16 text-muted-foreground"
                            >
                                <Camera class="size-10 opacity-25" />
                                <p class="text-sm">{{ t('calib.step2NoCamera') }}</p>
                            </div>

                            <!-- 相机列表（MiniProfiler 风格手风琴） -->
                            <div v-else class="flex flex-col gap-2">
                                <div
                                    v-for="cam in step2Cameras"
                                    :key="cam.id"
                                    class="border border-border/50 rounded-lg overflow-hidden"
                                >
                                    <!-- 相机行头部 -->
                                    <button
                                        class="w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-muted/30 transition-colors"
                                        :class="{ 'bg-muted/20': expandedCameraId === cam.id }"
                                        @click="toggleCameraExpand(cam.id)"
                                    >
                                        <Camera
                                            :class="[
                                                'size-4 shrink-0',
                                                cam.isEnabled ? 'text-blue-400' : 'text-muted-foreground/40',
                                            ]"
                                        />
                                        <span class="flex-1 font-medium text-sm">{{ cam.name }}</span>
                                        <!-- 型号 -->
                                        <span v-if="cam.model" class="text-xs text-muted-foreground hidden sm:inline">
                                            {{ cam.model }}
                                        </span>
                                        <!-- 连接状态 -->
                                        <span
                                            v-if="cameraConnecting[cam.id]"
                                            class="flex items-center gap-1 text-xs text-amber-400"
                                        >
                                            <Loader2 class="size-3 animate-spin" />
                                            {{ t('calib.step2CameraConnecting') }}
                                        </span>
                                        <span v-else :class="['text-xs', cameraStatusColor(cam.status)]">
                                            ● {{ cameraStatusLabel(cam.status) }}
                                        </span>
                                        <!-- 禁用标记 -->
                                        <span v-if="!cam.isEnabled" class="text-xs text-muted-foreground/50 ml-1">
                                            {{ t('calib.disabled') }}
                                        </span>
                                        <!-- 展开图标 -->
                                        <ChevronDown
                                            v-if="expandedCameraId === cam.id"
                                            class="size-4 shrink-0 text-muted-foreground"
                                        />
                                        <ChevronRight v-else class="size-4 shrink-0 text-muted-foreground" />
                                    </button>

                                    <!-- 展开内容 -->
                                    <div
                                        v-if="expandedCameraId === cam.id"
                                        class="border-t border-border/40 bg-background/20 px-5 py-4"
                                    >
                                        <!-- 已禁用提示 -->
                                        <div
                                            v-if="!cam.isEnabled"
                                            class="flex items-center gap-2 text-sm text-muted-foreground py-2"
                                        >
                                            <AlertCircle class="size-4 shrink-0" />
                                            {{ t('calib.step2CameraDisabled') }}
                                        </div>

                                        <!-- 正在连接中占位 -->
                                        <div
                                            v-else-if="cameraConnecting[cam.id]"
                                            class="flex items-center gap-2 text-sm text-muted-foreground py-2"
                                        >
                                            <Loader2 class="size-4 animate-spin" />
                                            {{ t('calib.step2CameraConnecting') }}
                                        </div>

                                        <!-- 参数表单 -->
                                        <div v-else class="space-y-5">
                                            <!-- 分区：传感器信息 -->
                                            <div>
                                                <h4
                                                    class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3"
                                                >
                                                    {{ t('calib.step2CmosSensorSize') }}
                                                </h4>
                                                <div class="grid grid-cols-1 sm:grid-cols-3 gap-x-6 gap-y-3 text-sm">
                                                    <!-- CMOS 尺寸选择 -->
                                                    <div class="sm:col-span-1">
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2CmosSensorSize') }}
                                                        </label>
                                                        <Select
                                                            v-model="cameraForms[cam.id].sensorSize"
                                                            :options="CMOS_SENSOR_SIZES"
                                                            :option-label="(item: CmosSensorSize) => t(item.labelKey)"
                                                            option-value="code"
                                                            :placeholder="t('common.pleaseSelect')"
                                                            size="small"
                                                            class="w-full !text-xs"
                                                            :pt="{
                                                                root: { class: '!py-0 !px-2 !h-7 !flex !items-center' },
                                                                label: { class: '!text-xs !py-0' },
                                                            }"
                                                        />
                                                    </div>
                                                    <!-- 传感器宽度(mm) 自动填入 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2SensorWidth') }}
                                                        </label>
                                                        <div
                                                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                        >
                                                            {{
                                                                getSelectedCmosSize(cameraForms[cam.id]?.sensorSize)
                                                                    ?.widthMm ?? '—'
                                                            }}
                                                        </div>
                                                    </div>
                                                    <!-- 传感器高度(mm) 自动填入 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2SensorHeight') }}
                                                        </label>
                                                        <div
                                                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                        >
                                                            {{
                                                                getSelectedCmosSize(cameraForms[cam.id]?.sensorSize)
                                                                    ?.heightMm ?? '—'
                                                            }}
                                                        </div>
                                                    </div>
                                                </div>
                                            </div>

                                            <!-- 分区：镜头参数 -->
                                            <div>
                                                <h4
                                                    class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3"
                                                >
                                                    {{ t('calib.step2FocalLength') }}
                                                </h4>
                                                <div class="grid grid-cols-2 sm:grid-cols-4 gap-x-6 gap-y-3 text-sm">
                                                    <!-- 镜头标称焦距 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2FocalLength') }}
                                                        </label>
                                                        <InputNumber
                                                            v-model="cameraForms[cam.id].lensFocalLength"
                                                            :min="0"
                                                            :max="10000"
                                                            :max-fraction-digits="2"
                                                            size="small"
                                                            class="w-full"
                                                            :input-class="'!text-xs !h-7 !py-0'"
                                                        />
                                                    </div>
                                                    <!-- 最大光圈 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2MaxAperture') }}
                                                        </label>
                                                        <InputNumber
                                                            v-model="cameraForms[cam.id].maxAperture"
                                                            :min="0.7"
                                                            :max="64"
                                                            :max-fraction-digits="1"
                                                            size="small"
                                                            class="w-full"
                                                            :input-class="'!text-xs !h-7 !py-0'"
                                                        />
                                                    </div>
                                                    <!-- 最小光圈 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2MinAperture') }}
                                                        </label>
                                                        <InputNumber
                                                            v-model="cameraForms[cam.id].minAperture"
                                                            :min="0.7"
                                                            :max="64"
                                                            :max-fraction-digits="1"
                                                            size="small"
                                                            class="w-full"
                                                            :input-class="'!text-xs !h-7 !py-0'"
                                                        />
                                                    </div>
                                                    <!-- 当前光圈 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2CurrentAperture') }}
                                                        </label>
                                                        <InputNumber
                                                            v-model="cameraForms[cam.id].currentAperture"
                                                            :min="0.7"
                                                            :max="64"
                                                            :max-fraction-digits="1"
                                                            size="small"
                                                            class="w-full"
                                                            :input-class="'!text-xs !h-7 !py-0'"
                                                        />
                                                    </div>
                                                </div>
                                            </div>

                                            <!-- 分区：从相机读取的硬件参数 -->
                                            <div>
                                                <div class="flex items-center justify-between mb-3">
                                                    <h4
                                                        class="text-xs font-semibold text-muted-foreground uppercase tracking-wide"
                                                    >
                                                        {{ t('calib.step2ImageResolution') }} &amp;
                                                        {{ t('calib.step2ExposureRange') }}
                                                    </h4>
                                                    <Button
                                                        text
                                                        severity="secondary"
                                                        size="small"
                                                        :disabled="hardwareLoading[cam.id]"
                                                        @click="readHardwareParams(cam)"
                                                    >
                                                        <Loader2
                                                            v-if="hardwareLoading[cam.id]"
                                                            class="size-3 animate-spin mr-1"
                                                        />
                                                        <RefreshCcw v-else class="size-3 mr-1" />
                                                        <span class="text-xs">
                                                            {{
                                                                hardwareLoading[cam.id]
                                                                    ? t('calib.step2ReadingCamera')
                                                                    : t('calib.step2ReadFromCamera')
                                                            }}
                                                        </span>
                                                    </Button>
                                                </div>
                                                <div class="grid grid-cols-2 sm:grid-cols-3 gap-x-6 gap-y-3 text-sm">
                                                    <!-- 分辨率 宽 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2ImageWidth') }}
                                                        </label>
                                                        <div
                                                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                        >
                                                            {{ cameraHardware[cam.id]?.imageWidthPixels ?? '—' }}
                                                        </div>
                                                    </div>
                                                    <!-- 分辨率 高 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2ImageHeight') }}
                                                        </label>
                                                        <div
                                                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                        >
                                                            {{ cameraHardware[cam.id]?.imageHeightPixels ?? '—' }}
                                                        </div>
                                                    </div>
                                                    <!-- 像素尺寸（计算值） -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2PixelSizeCalc') }}
                                                        </label>
                                                        <div
                                                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                        >
                                                            {{ calcPixelSize(cam.id) ?? '—' }}
                                                        </div>
                                                    </div>
                                                    <!-- 最小曝光时间 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2ExposureMin') }} (μs)
                                                        </label>
                                                        <div
                                                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                        >
                                                            {{ cameraHardware[cam.id]?.exposureTimeMinUs ?? '—' }}
                                                        </div>
                                                    </div>
                                                    <!-- 最大曝光时间 -->
                                                    <div>
                                                        <label class="block text-xs text-muted-foreground mb-1">
                                                            {{ t('calib.step2ExposureMax') }} (μs)
                                                        </label>
                                                        <div
                                                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                        >
                                                            {{ cameraHardware[cam.id]?.exposureTimeMaxUs ?? '—' }}
                                                        </div>
                                                    </div>
                                                </div>
                                            </div>

                                            <!-- 保存按钮 -->
                                            <div class="flex justify-end pt-2">
                                                <Button
                                                    size="small"
                                                    :disabled="cameraSaving[cam.id]"
                                                    @click="saveCameraParams(cam)"
                                                >
                                                    <Loader2
                                                        v-if="cameraSaving[cam.id]"
                                                        class="size-3.5 animate-spin mr-1.5"
                                                    />
                                                    <Save v-else class="size-3.5 mr-1.5" />
                                                    {{
                                                        cameraSaving[cam.id]
                                                            ? t('calib.step2Saving')
                                                            : t('calib.step2SaveParam')
                                                    }}
                                                </Button>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Step 2 底部导航 -->
                        <div class="flex justify-between gap-2 px-4 py-3 border-t border-border/40 mt-auto">
                            <Button severity="secondary" outlined size="small" @click="activeStep = '1'">
                                {{ t('calib.prevStep') }}
                            </Button>
                            <Button size="small" @click="activeStep = '3'">
                                {{ t('calib.nextStep') }}
                            </Button>
                        </div>
                    </StepPanel>

                    <!-- ============ Step 3 投影机参数 ============ -->
                    <StepPanel
                        value="3"
                        class="flex flex-col flex-1 min-h-0"
                        :pt="{ root: { class: '!bg-transparent' } }"
                    >
                        <div v-if="step3Loading" class="flex flex-1 items-center justify-center">
                            <Loader2 class="size-6 animate-spin text-muted-foreground" />
                        </div>
                        <template v-else>
                            <div class="flex flex-1 min-h-0 overflow-hidden">
                                <!-- 左侧：投影机选择 + 光栅设置 -->
                                <div
                                    class="w-72 shrink-0 border-r border-border/40 flex flex-col overflow-y-auto p-4 gap-4"
                                >
                                    <!-- 投影机选择 -->
                                    <div>
                                        <label class="block text-xs text-muted-foreground mb-1.5">
                                            {{ t('calib.step3ProjectorSelect') }}
                                        </label>
                                        <div
                                            v-if="step3Projectors.length === 0"
                                            class="flex items-center gap-1.5 text-xs text-amber-400"
                                        >
                                            <AlertCircle class="size-3.5 shrink-0" />
                                            {{ t('calib.step3NoProjector') }}
                                        </div>
                                        <Select
                                            v-else
                                            v-model="selectedProjectorId"
                                            :options="step3Projectors"
                                            option-label="name"
                                            option-value="id"
                                            size="small"
                                            class="w-full !text-xs"
                                            :pt="{
                                                root: { class: '!py-0 !px-2 !h-7 !flex !items-center' },
                                                label: { class: '!text-xs !py-0' },
                                            }"
                                        />
                                    </div>

                                    <!-- 光栅设置面板 -->
                                    <div class="rounded-lg border border-border/40 bg-muted/10 p-3 flex flex-col gap-3">
                                        <h3 class="text-xs font-semibold text-foreground/80">
                                            {{ t('calib.step3FringeSettings') }}
                                        </h3>

                                        <!-- 条纹模式 -->
                                        <div>
                                            <label class="block text-xs text-muted-foreground mb-1">
                                                {{ t('calib.step3FringeMode') }}
                                            </label>
                                            <div class="flex gap-1.5">
                                                <Button
                                                    :severity="fringeMode === 'horizontal' ? 'primary' : 'secondary'"
                                                    size="small"
                                                    class="!text-xs flex-1"
                                                    @click="fringeMode = 'horizontal'"
                                                >
                                                    {{ t('calib.step3FringeModeH') }}
                                                </Button>
                                                <Button
                                                    :severity="fringeMode === 'vertical' ? 'primary' : 'secondary'"
                                                    size="small"
                                                    class="!text-xs flex-1"
                                                    @click="fringeMode = 'vertical'"
                                                >
                                                    {{ t('calib.step3FringeModeV') }}
                                                </Button>
                                            </div>
                                        </div>

                                        <!-- 条纹类型 -->
                                        <div>
                                            <label class="block text-xs text-muted-foreground mb-1">
                                                {{ t('calib.step3FringeType') }}
                                            </label>
                                            <div class="flex gap-1.5">
                                                <Button
                                                    :severity="fringeType === 'bw' ? 'primary' : 'secondary'"
                                                    size="small"
                                                    class="!text-xs flex-1"
                                                    @click="fringeType = 'bw'"
                                                >
                                                    {{ t('calib.step3FringeTypeBW') }}
                                                </Button>
                                                <Button
                                                    :severity="fringeType === 'wb' ? 'primary' : 'secondary'"
                                                    size="small"
                                                    class="!text-xs flex-1"
                                                    @click="fringeType = 'wb'"
                                                >
                                                    {{ t('calib.step3FringeTypeWB') }}
                                                </Button>
                                            </div>
                                        </div>

                                        <!-- 宽度像素（只读 + 从光机读取） -->
                                        <div>
                                            <label class="block text-xs text-muted-foreground mb-1">
                                                {{ t('calib.step3WidthPixels') }}
                                            </label>
                                            <div class="flex gap-1.5 items-center">
                                                <div
                                                    class="flex-1 h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                                >
                                                    {{
                                                        projectorWidthPixels != null
                                                            ? `${projectorWidthPixels} px`
                                                            : '—'
                                                    }}
                                                </div>
                                                <Button
                                                    severity="secondary"
                                                    outlined
                                                    size="small"
                                                    :disabled="!selectedProjectorId || projectorReading"
                                                    class="!text-xs shrink-0"
                                                    @click="void fetchProjectorResolution()"
                                                >
                                                    <Loader2 v-if="projectorReading" class="size-3 animate-spin mr-1" />
                                                    {{
                                                        projectorReading
                                                            ? t('calib.step3Reading')
                                                            : t('calib.step3GetFromProjector')
                                                    }}
                                                </Button>
                                            </div>
                                            <div
                                                v-if="projectorPixelMode"
                                                class="text-[10px] text-muted-foreground mt-1"
                                            >
                                                {{ t('calib.step3PixelMode') }}: {{ projectorPixelMode }}
                                            </div>
                                        </div>

                                        <!-- 高度像素（用户输入） -->
                                        <div>
                                            <label class="block text-xs text-muted-foreground mb-1">
                                                {{ t('calib.step3HeightPixels') }}
                                            </label>
                                            <InputNumber
                                                v-model="projectorHeightInput"
                                                :min="1"
                                                :max="10000"
                                                :max-fraction-digits="0"
                                                size="small"
                                                class="w-full"
                                                :input-class="'!text-xs !h-7 !py-0'"
                                            />
                                        </div>

                                        <!-- 周期数 -->
                                        <div>
                                            <label class="block text-xs text-muted-foreground mb-1">
                                                {{ t('calib.step3PeriodCount') }}
                                            </label>
                                            <InputNumber
                                                v-model="fringe3PeriodCount"
                                                :min="1"
                                                :max="1000"
                                                :max-fraction-digits="0"
                                                size="small"
                                                class="w-full"
                                                :input-class="'!text-xs !h-7 !py-0'"
                                            />
                                            <p v-if="fringe3PeriodError" class="text-[10px] text-red-400 mt-1">
                                                {{ fringe3PeriodError }}
                                            </p>
                                        </div>

                                        <!-- 生成图片数量 -->
                                        <div>
                                            <label class="block text-xs text-muted-foreground mb-1">
                                                {{ t('calib.step3ImageCount') }}
                                            </label>
                                            <InputNumber
                                                v-model="fringe3ImageCount"
                                                :min="1"
                                                :max="100"
                                                :max-fraction-digits="0"
                                                size="small"
                                                class="w-full"
                                                :input-class="'!text-xs !h-7 !py-0'"
                                            />
                                        </div>

                                        <!-- 相移 -->
                                        <div>
                                            <label class="block text-xs text-muted-foreground mb-1">
                                                {{ t('calib.step3PhaseShift') }}
                                            </label>
                                            <InputNumber
                                                v-model="fringe3PhaseShift"
                                                :min="1"
                                                :max="fringe3PeriodCount - 1"
                                                :max-fraction-digits="0"
                                                size="small"
                                                class="w-full"
                                                :input-class="'!text-xs !h-7 !py-0'"
                                            />
                                            <p v-if="fringe3PhaseError" class="text-[10px] text-red-400 mt-1">
                                                {{ fringe3PhaseError }}
                                            </p>
                                        </div>
                                    </div>

                                    <!-- 操作按钮 -->
                                    <div class="flex gap-2">
                                        <Button
                                            size="small"
                                            :disabled="!fringe3CanGenerate || generatingFringe"
                                            class="flex-1 !text-xs"
                                            @click="generateFringeImages"
                                        >
                                            <Loader2 v-if="generatingFringe" class="size-3 animate-spin mr-1.5" />
                                            {{ t('calib.step3GenerateImages') }}
                                        </Button>
                                        <Button
                                            severity="secondary"
                                            outlined
                                            size="small"
                                            :disabled="
                                                !generatedFringeImages.length ||
                                                !projectorWidthPixels ||
                                                downloadingFringe
                                            "
                                            class="flex-1 !text-xs"
                                            @click="void triggerFringeDownload()"
                                        >
                                            <Loader2 v-if="downloadingFringe" class="size-3 animate-spin mr-1.5" />
                                            {{
                                                downloadingFringe
                                                    ? `${t('calib.step3Downloading')} ${fringeDownloadProgress}%`
                                                    : t('calib.step3DownloadImages')
                                            }}
                                        </Button>
                                    </div>
                                </div>

                                <!-- 右侧：图像列表 + 预览 -->
                                <div class="flex flex-1 min-w-0 min-h-0 overflow-hidden">
                                    <!-- 图像列表 -->
                                    <div
                                        class="w-28 shrink-0 border-r border-border/40 overflow-y-auto p-2 flex flex-col gap-1"
                                    >
                                        <div class="text-[10px] text-muted-foreground px-1 mb-1">
                                            {{ t('calib.step3ImageList') }}
                                        </div>
                                        <div
                                            v-if="generatedFringeImages.length === 0"
                                            class="text-[10px] text-muted-foreground/60 text-center py-4"
                                        >
                                            {{ t('calib.step3NoImages') }}
                                        </div>
                                        <button
                                            v-for="img in generatedFringeImages"
                                            :key="img.index"
                                            :class="[
                                                'rounded px-2 py-1 text-left text-xs transition-colors',
                                                selectedFringeImageIdx === img.index
                                                    ? 'bg-primary/10 text-primary'
                                                    : 'text-muted-foreground hover:bg-muted/20',
                                            ]"
                                            @click="selectedFringeImageIdx = img.index"
                                        >
                                            {{ img.label }}
                                        </button>
                                    </div>

                                    <!-- 预览画布 -->
                                    <div class="flex flex-1 flex-col min-w-0 min-h-0 p-4">
                                        <div class="text-[10px] text-muted-foreground mb-2">
                                            {{ t('calib.step3ImagePreview') }}
                                        </div>
                                        <div
                                            class="flex-1 min-h-0 flex items-center justify-center rounded border border-border/40 bg-black/20"
                                        >
                                            <canvas
                                                v-if="generatedFringeImages.length > 0"
                                                ref="previewCanvasRef"
                                                :width="projectorWidthPixels ?? 512"
                                                :height="projectorHeightInput || 512"
                                                class="max-w-full max-h-full"
                                                style="image-rendering: pixelated; object-fit: contain"
                                            />
                                            <div v-else class="text-xs text-muted-foreground/50">
                                                {{ t('calib.step3NoImages') }}
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>

                            <!-- Step 3 底部导航 -->
                            <div class="flex justify-between gap-2 px-4 py-3 border-t border-border/40 mt-auto">
                                <Button severity="secondary" outlined size="small" @click="activeStep = '2'">
                                    {{ t('calib.prevStep') }}
                                </Button>
                                <Button size="small" @click="activeStep = '4'">
                                    {{ t('calib.nextStep') }}
                                </Button>
                            </div>
                        </template>
                    </StepPanel>

                    <!-- ============ Step 4 ~ 7 占位 ============ -->
                    <StepPanel
                        v-for="n in ['4', '5', '6', '7']"
                        :key="n"
                        :value="n"
                        class="flex flex-col flex-1 min-h-0"
                        :pt="{ root: { class: '!bg-transparent' } }"
                    >
                        <div class="flex flex-1 flex-col items-center justify-center py-16 text-muted-foreground">
                            <span class="text-4xl mb-3 opacity-30">🚧</span>
                            <p class="text-sm">{{ t('calib.stepComingSoon') }}</p>
                        </div>
                        <div class="flex justify-between gap-2 px-4 py-3 border-t border-border/40 mt-auto">
                            <Button
                                severity="secondary"
                                outlined
                                size="small"
                                @click="activeStep = String(Number(n) - 1)"
                            >
                                {{ t('calib.prevStep') }}
                            </Button>
                            <Button v-if="n !== '7'" size="small" @click="activeStep = String(Number(n) + 1)">
                                {{ t('calib.nextStep') }}
                            </Button>
                        </div>
                    </StepPanel>
                </StepPanels>
            </Stepper>
        </div>
    </div>
</template>
