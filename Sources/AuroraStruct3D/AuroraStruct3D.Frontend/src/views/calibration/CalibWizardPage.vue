<script setup lang="ts">
/**
 * 标定向导页
 * 使用 PrimeVue Stepper 引导用户完成多步标定流程
 * Step 1：设备标定信息（左侧项目信息 + 右侧设备布局SVG图）
 * Step 2：相机参数配置（列出所有相机，可展开设置各相机镜头/传感器参数）
 */
import { ref, computed, onMounted, watch, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Stepper from 'primevue/stepper'
import StepList from 'primevue/steplist'
import Step from 'primevue/step'
import StepPanels from 'primevue/steppanels'
import StepPanel from 'primevue/steppanel'
import BorderBeam from '@/components/ui/border-beam/BorderBeam.vue'
import CalibStep1ProjectInfo from './CalibStep1ProjectInfo.vue'
import CalibStep2CameraConfig from './CalibStep2CameraConfig.vue'
import CalibStep3ProjectorConfig from './CalibStep3ProjectorConfig.vue'
import CalibStep4MotorConfig from './CalibStep4MotorConfig.vue'
import CalibStep5CameraCalib from './CalibStep5CameraCalib.vue'
import CalibStep6OnlineScan from './CalibStep6OnlineScan.vue'
import CalibStep7PointCloud from './CalibStep7PointCloud.vue'
import CalibStepComingSoon from './CalibStepComingSoon.vue'
import { ArrowLeft } from '@lucide/vue'
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
import {
    getCalibProjectorParam,
    updateCalibProjectorParam,
    type SaveCalibProjectorParamInput,
} from '@/api/calib-projector-param'

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
        selectedProjectorId.value = project.value?.boundProjectorDeviceId ?? null
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
        [CalibDeviceType.OneCamera1Light]: t('calib.type1C1L'),
        [CalibDeviceType.TwoCamera1Light]: t('calib.type2C1L'),
    }
    return map[type] ?? String(type)
}

// ===================== Stepper 状态 =====================

const activeStep = ref('1')

type WizardStepKind = 'project' | 'camera' | 'projector' | 'motor' | 'calib' | 'scan' | 'pointcloud' | 'soon'

interface WizardStepItem {
    value: string
    label: string
    kind: WizardStepKind
}

const showProjectorStep = computed(
    () => project.value?.deviceSeries === DeviceSeries.SingleLight && !!project.value?.boundProjectorDeviceId
)

const stepItems = computed<WizardStepItem[]>(() => {
    if (showProjectorStep.value) {
        return [
            { value: '1', label: t('calib.step1Label'), kind: 'project' },
            { value: '2', label: t('calib.step2Label'), kind: 'camera' },
            { value: '3', label: t('calib.step3Label'), kind: 'projector' },
            { value: '4', label: t('calib.step4Label'), kind: 'motor' },
            { value: '5', label: t('calib.step5Label'), kind: 'calib' },
            { value: '6', label: t('calib.step6Label'), kind: 'scan' },
            { value: '7', label: t('calib.step7Label'), kind: 'pointcloud' },
        ]
    }

    return [
        { value: '1', label: t('calib.step1Label'), kind: 'project' },
        { value: '2', label: t('calib.step2Label'), kind: 'camera' },
        { value: '3', label: t('calib.step4Label'), kind: 'motor' },
        { value: '4', label: t('calib.step5Label'), kind: 'calib' },
        { value: '5', label: t('calib.step6Label'), kind: 'scan' },
        { value: '6', label: t('calib.step7Label'), kind: 'pointcloud' },
    ]
})

const stepValueMap = computed<Record<WizardStepKind, string>>(() => {
    return stepItems.value.reduce<Record<WizardStepKind, string>>(
        (map, item) => {
            map[item.kind] = item.value
            return map
        },
        {} as Record<WizardStepKind, string>
    )
})

function getStepValue(kind: WizardStepKind): string | null {
    return stepValueMap.value[kind] ?? null
}

function goToStep(kind: WizardStepKind): void {
    const value = getStepValue(kind)
    if (value) {
        activeStep.value = value
    }
}

function goToNumericStep(stepNumber: number): void {
    activeStep.value = String(stepNumber)
}

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
        const boundIds = new Set<string>(
            [project.value?.mainCameraDeviceId, project.value?.secondaryCameraDeviceId].filter(
                (id): id is string => id !== null && id !== undefined
            )
        )
        step2Cameras.value = result.items.filter((c) => c.isEnabled && boundIds.has(c.id))

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

// ===================== Step 2 — 项目绑定校验与 Next 处理 =====================

function validateProjectBindingsForStep2(): string | null {
    if (!project.value) {
        return '项目未加载完成'
    }

    const dt = project.value.deviceType
    if (!project.value.mainCameraDeviceId) {
        return '请先在项目管理页绑定主相机'
    }

    if (!project.value.mainCameraMotorAxisId) {
        return '请先在项目管理页绑定主相机角度控制电机'
    }

    if (!project.value.distanceMotorAxisId) {
        return '请先在项目管理页绑定间距控制电机'
    }

    if (dt === CalibDeviceType.OneCamera1Light) {
        if (project.value.secondaryCameraDeviceId || project.value.secondaryCameraMotorAxisId) {
            return '1目1光不允许绑定从相机或从相机角度控制电机'
        }
        if (!project.value.boundProjectorDeviceId) {
            return '请先在项目管理页绑定主结构光机'
        }
    }

    if (dt === CalibDeviceType.TwoCamera0Light || dt === CalibDeviceType.TwoCamera1Light) {
        if (!project.value.secondaryCameraDeviceId) {
            return '请先在项目管理页绑定从相机'
        }
        if (!project.value.secondaryCameraMotorAxisId) {
            return '请先在项目管理页绑定从相机角度控制电机'
        }
    }

    if (dt === CalibDeviceType.TwoCamera1Light && !project.value.boundProjectorDeviceId) {
        return '请先在项目管理页绑定主结构光机'
    }

    return null
}

const step2Saving = ref(false)

/** Step 2 Next：校验位置绑定 → 全量保存所有相机参数（含位置） → 前进 */
async function handleStep2Next(): Promise<void> {
    const error = validateProjectBindingsForStep2()
    if (error) {
        const { error: toastError } = useAppToast()
        toastError(error)
        return
    }
    if (!project.value || step2Saving.value) return
    step2Saving.value = true
    try {
        await Promise.all(
            step2Cameras.value.map(async (cam: CameraDeviceDto) => {
                const form = cameraForms.value[cam.id]
                const hw = cameraHardware.value[cam.id]
                const cmos = getSelectedCmosSize(form?.sensorSize ?? null)
                await saveCalibCameraParamAsync({
                    calibProjectId: project.value!.id,
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
            })
        )
        if (showProjectorStep.value) {
            goToStep('projector')
        } else {
            goToStep('motor')
        }
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        step2Saving.value = false
    }
}

// ===================== Step 3 投影仪参数 =====================

/** Step 3 Next（单光系列）：校验项目已绑定主结构光机 → 保存投影仪参数 → 前进 */
async function handleStep3Next(): Promise<void> {
    if (project.value?.projectorCount && project.value.projectorCount > 0) {
        if (!project.value.boundProjectorDeviceId) {
            const { error: toastError } = useAppToast()
            toastError('单光系列必须先在项目管理页绑定主结构光机才能继续')
            return
        }
    }

    // 保存投影仪参数到数据库（Upsert），失败不阻断前进流程，仅提示
    const projectId = project.value?.id
    const projectorId = selectedProjectorId.value
    if (projectId && projectorId) {
        try {
            const input: SaveCalibProjectorParamInput = {
                calibProjectId: projectId,
                projectorDeviceId: projectorId,
                resolutionWidth: projectorWidthPixels.value ?? 0,
                resolutionHeight: projectorHeightInput.value,
                periodCount: fringe3PeriodCount.value,
                fringeType: fringeType.value,
                patternCount: fringe3ImageCount.value,
                phaseShift: fringe3PhaseShift.value,
            }
            await updateCalibProjectorParam(projectId, input)
        } catch (e) {
            showErrorToastOnce(e)
        }
    }

    goToStep('motor')
}
const step3Loading = ref(false)
const step3Projectors = ref<ProjectorDeviceDto[]>([])
const selectedProjectorId = ref<string | null>(null)
const projectorWidthPixels = ref<number | null>(null)
const projectorPixelMode = ref<string | null>(null)
const projectorReading = ref(false)

/** 条纹类型：bw=黑白（首色黑）wb=白黑（首色白） */
const fringeType = ref<'bw' | 'wb'>('bw')
const projectorHeightInput = ref<number>(720)
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

/** Step3 固定按 121212 横竖交替生成；像素序列统一按投影宽度计算。 */
const fringe3PixelCount = computed(() => projectorWidthPixels.value)

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
    return projectorWidthPixels.value != null && projectorHeightInput.value > 0
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
        const boundProjectorId = project.value?.boundProjectorDeviceId ?? null
        step3Projectors.value = result.items.filter(
            (p) => p.isEnabled && (!boundProjectorId || p.id === boundProjectorId)
        )
        if (boundProjectorId) {
            selectedProjectorId.value = boundProjectorId
        }

        // 回显已保存的投影仪参数（Step3 配置页持久化）
        const projectId = project.value?.id
        if (projectId) {
            try {
                const saved = await getCalibProjectorParam(projectId)
                if (saved) {
                    if (saved.resolutionWidth > 0) {
                        projectorWidthPixels.value = saved.resolutionWidth
                    }
                    if (saved.resolutionHeight > 0) {
                        projectorHeightInput.value = saved.resolutionHeight
                    }
                    if (saved.periodCount > 0) {
                        fringe3PeriodCount.value = saved.periodCount
                    }
                    if (saved.patternCount > 0) {
                        fringe3ImageCount.value = saved.patternCount
                    }
                    if (saved.phaseShift != null && saved.phaseShift > 0) {
                        fringe3PhaseShift.value = saved.phaseShift
                    }
                    if (saved.fringeType === 'bw' || saved.fringeType === 'wb') {
                        fringeType.value = saved.fringeType
                    }
                    if (saved.projectorDeviceId) {
                        selectedProjectorId.value = saved.projectorDeviceId
                    }
                }
            } catch {
                /* 回显失败不阻断页面，使用默认值 */
            }
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
            fringeMode: 'horizontal',
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

async function triggerFringeDownload(): Promise<void> {
    if (!selectedProjectorId.value || !projectorWidthPixels.value || downloadingFringe.value) return
    downloadingFringe.value = true
    fringeDownloadProgress.value = 0
    try {
        await downloadFringePattern({
            projectorId: selectedProjectorId.value,
            fringeMode: 'horizontal',
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

// 切换步骤时自动初始化对应步骤
watch(activeStep, (val: string) => {
    if (val === '2') void initStep2()
    if (val === getStepValue('projector')) void initStep3()
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
        <div class="flex items-center gap-3">
            <Button text severity="secondary" size="small" @click="goBack">
                <ArrowLeft class="size-4" />
            </Button>
            <h1 class="text-2xl font-bold tracking-tight">
                {{ project?.name ?? t('calib.calibrate') }}
            </h1>
        </div>

        <div
            class="relative flex flex-col flex-1 min-h-0 overflow-hidden rounded-xl bg-card/40 backdrop-blur border border-border shadow-sm"
            style="clip-path: inset(0 round 0.75rem)"
        >
            <BorderBeam :size="120" :duration="10" />
            <Stepper
                v-model:value="activeStep"
                linear
                class="flex flex-col flex-1 min-h-0"
                :pt="{
                    root: {
                        class: '!bg-transparent !border-0 !shadow-none !rounded-none flex flex-col flex-1 min-h-0',
                    },
                }"
            >
                <StepList class="border-b border-border/40 px-4 pt-3" :pt="{ root: { class: '!bg-transparent' } }">
                    <Step
                        v-for="item in stepItems"
                        :key="item.value"
                        :value="item.value"
                        :pt="{ root: { class: '!bg-transparent' } }"
                    >
                        {{ item.label }}
                    </Step>
                </StepList>

                <StepPanels class="flex flex-col flex-1 min-h-0" :pt="{ root: { class: '!bg-transparent' } }">
                    <StepPanel
                        v-for="item in stepItems"
                        :key="item.value"
                        :value="item.value"
                        class="flex flex-col flex-1 min-h-0"
                        :pt="{ root: { class: '!bg-transparent' } }"
                    >
                        <template v-if="item.kind === 'project'">
                            <CalibStep1ProjectInfo
                                :loading="loading"
                                :project="project"
                                :device-series-label="deviceSeriesLabel"
                                :device-type-label="deviceTypeLabel"
                                @next="goToStep('camera')"
                            />
                        </template>
                        <template v-else-if="item.kind === 'camera'">
                            <CalibStep2CameraConfig
                                :step2-loading="step2Loading"
                                :step2-cameras="step2Cameras"
                                :expanded-camera-id="expandedCameraId"
                                :camera-connecting="cameraConnecting"
                                :camera-forms="cameraForms"
                                :camera-hardware="cameraHardware"
                                :hardware-loading="hardwareLoading"
                                :camera-saving="cameraSaving"
                                :cmos-sensor-sizes="CMOS_SENSOR_SIZES"
                                :get-selected-cmos-size="getSelectedCmosSize"
                                :calc-pixel-size="calcPixelSize"
                                :toggle-camera-expand="toggleCameraExpand"
                                :read-hardware-params="readHardwareParams"
                                :save-camera-params="saveCameraParams"
                                :camera-status-color="cameraStatusColor"
                                :camera-status-label="cameraStatusLabel"
                                @prev="goToStep('project')"
                                @next="handleStep2Next"
                            />
                        </template>
                        <template v-else-if="item.kind === 'projector'">
                            <CalibStep3ProjectorConfig
                                :step3-loading="step3Loading"
                                :step3-projectors="step3Projectors"
                                :selected-projector-id="selectedProjectorId"
                                :projector-width-pixels="projectorWidthPixels"
                                :projector-pixel-mode="projectorPixelMode"
                                :projector-reading="projectorReading"
                                :fringe-type="fringeType"
                                :projector-height-input="projectorHeightInput"
                                :fringe3-period-count="fringe3PeriodCount"
                                :fringe3-image-count="fringe3ImageCount"
                                :fringe3-phase-shift="fringe3PhaseShift"
                                :fringe3-period-error="fringe3PeriodError"
                                :fringe3-phase-error="fringe3PhaseError"
                                :fringe3-can-generate="fringe3CanGenerate"
                                :generating-fringe="generatingFringe"
                                :generated-fringe-images="generatedFringeImages"
                                :selected-fringe-image-idx="selectedFringeImageIdx"
                                :downloading-fringe="downloadingFringe"
                                :fringe-download-progress="fringeDownloadProgress"
                                :fetch-projector-resolution="fetchProjectorResolution"
                                :generate-fringe-images="generateFringeImages"
                                :trigger-fringe-download="triggerFringeDownload"
                                @prev="goToStep('camera')"
                                @next="handleStep3Next"
                                @update:selected-projector-id="selectedProjectorId = $event"
                                @update:fringe-type="fringeType = $event"
                                @update:projector-height-input="projectorHeightInput = $event"
                                @update:fringe3-period-count="fringe3PeriodCount = $event"
                                @update:fringe3-image-count="fringe3ImageCount = $event"
                                @update:fringe3-phase-shift="fringe3PhaseShift = $event"
                                @update:selected-fringe-image-idx="selectedFringeImageIdx = $event"
                            />
                        </template>
                        <template v-else-if="item.kind === 'motor'">
                            <div class="flex flex-1 min-h-0 flex-col">
                                <CalibStep4MotorConfig v-if="project" :project="project" />
                            </div>
                            <div class="mt-auto flex justify-between gap-2 border-t border-border/40 px-4 py-3">
                                <Button
                                    severity="secondary"
                                    outlined
                                    size="small"
                                    @click="goToStep(showProjectorStep ? 'projector' : 'camera')"
                                >
                                    {{ t('calib.prevStep') }}
                                </Button>
                                <Button size="small" @click="goToStep('calib')">
                                    {{ t('calib.nextStep') }}
                                </Button>
                            </div>
                        </template>
                        <template v-else-if="item.kind === 'calib'">
                            <div class="flex flex-1 min-h-0 flex-col">
                                <CalibStep5CameraCalib
                                    v-if="project"
                                    :project="project"
                                    :selected-projector-id="selectedProjectorId"
                                />
                            </div>
                            <div class="mt-auto flex justify-between gap-2 border-t border-border/40 px-4 py-3">
                                <Button severity="secondary" outlined size="small" @click="goToStep('motor')">
                                    {{ t('calib.prevStep') }}
                                </Button>
                                <Button size="small" @click="goToNumericStep(Number(item.value) + 1)">
                                    {{ t('calib.nextStep') }}
                                </Button>
                            </div>
                        </template>
                        <template v-else-if="item.kind === 'scan'">
                            <div class="flex flex-1 min-h-0 flex-col">
                                <CalibStep6OnlineScan v-if="project" :project="project" />
                            </div>
                            <div class="mt-auto flex justify-between gap-2 border-t border-border/40 px-4 py-3">
                                <Button severity="secondary" outlined size="small" @click="goToStep('calib')">
                                    {{ t('calib.prevStep') }}
                                </Button>
                                <Button size="small" @click="goToStep('pointcloud')">
                                    {{ t('calib.nextStep') }}
                                </Button>
                            </div>
                        </template>
                        <template v-else-if="item.kind === 'pointcloud'">
                            <div class="flex flex-1 min-h-0 flex-col">
                                <CalibStep7PointCloud v-if="project" :project="project" />
                            </div>
                            <div class="mt-auto flex justify-between gap-2 border-t border-border/40 px-4 py-3">
                                <Button severity="secondary" outlined size="small" @click="goToStep('scan')">
                                    {{ t('calib.prevStep') }}
                                </Button>
                            </div>
                        </template>
                        <template v-else>
                            <CalibStepComingSoon
                                :step="item.value"
                                :show-next="item.value !== stepItems[stepItems.length - 1].value"
                                @prev="goToNumericStep(Number(item.value) - 1)"
                                @next="goToNumericStep(Number(item.value) + 1)"
                            />
                        </template>
                    </StepPanel>
                </StepPanels>
            </Stepper>
        </div>
    </div>
</template>
