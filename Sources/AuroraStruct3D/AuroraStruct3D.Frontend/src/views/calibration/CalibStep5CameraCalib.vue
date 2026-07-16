<script setup lang="ts">
import { computed, ref, reactive, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Checkbox from 'primevue/checkbox'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Tag from 'primevue/tag'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import {
    Camera,
    ChevronDown,
    ChevronRight,
    Download,
    Lightbulb,
    LightbulbOff,
    Loader2,
    RefreshCcw,
    Trash2,
    Upload,
    Zap,
} from '@lucide/vue'
import { useAppToast } from '@/composables/useAppToast'
import { CalibDeviceType, DeviceSeries, type CalibProjectDto } from '@/api/calibration'
import { CameraStatus, type CameraDeviceDto, getCameraList } from '@/api/cameras'
import { projectorLedOn as ledOn, projectorLedOff as ledOff } from '@/api/projectors'
import {
    CalibrationBoardType,
    type CalibBoardConfigDto,
    type CalibCameraStatusDto,
    type CalibComputeResultDto,
    type CalibExtrinsicSampleDto,
    type CalibPhotoDto,
    type CalibStereoComputeResultDto,
    type CalibStereoStatusDto,
    CalibPhotoType,
    ExtrinsicPhotoPhase,
    computeIntrinsic,
    computeExtrinsic,
    computeStereoCalibration,
    deletePhoto,
    deleteInvalidPhotos,
    exportBoardConfig,
    getBoardConfig,
    getCameraStatus,
    getPhotoList,
    importBoardConfig,
    getStereoStatus,
    takeStereoExtrinsicPairPhoto,
    takeExtrinsicDotPhoto,
    takeExtrinsicCheckerboardPhoto,
    takeIntrinsicPhoto,
    updateBoardConfig,
} from '@/api/calib-photo'

// ─── Props ───────────────────────────────────────────────────────────────────

const props = defineProps<{
    project: CalibProjectDto
    /** Step 3 中选定的投影仪 ID（无投影仪时为 null） */
    selectedProjectorId: string | null
}>()

const emit = defineEmits<{
    prev: []
    next: []
}>()

// ─── Composables ─────────────────────────────────────────────────────────────

const { t } = useI18n()
const toast = useAppToast()
const confirm = useConfirm()

const activeProjectorId = computed<string | null>(() => {
    if (props.project.deviceSeries !== DeviceSeries.SingleLight) {
        return null
    }
    return props.selectedProjectorId ?? props.project.boundProjectorDeviceId
})

const showProjectorSection = computed(() => {
    return props.project.deviceSeries === DeviceSeries.SingleLight && !!activeProjectorId.value
})

function canUseProjectorExtrinsic(cameraId: string): boolean {
    if (!showProjectorSection.value) {
        return false
    }

    // 2目1光：仅主相机执行投影外参与外参拍照
    if (props.project.deviceType === CalibDeviceType.TwoCamera1Light) {
        return props.project.mainCameraDeviceId === cameraId
    }

    return true
}

const isStereoProject = computed(() => {
    return (
        props.project.deviceType === CalibDeviceType.TwoCamera0Light ||
        props.project.deviceType === CalibDeviceType.TwoCamera1Light
    )
})

function cameraRoleLabel(cameraId: string): string {
    if (props.project.mainCameraDeviceId === cameraId) {
        return '主相机'
    }
    if (props.project.secondaryCameraDeviceId === cameraId) {
        return '从相机'
    }
    return '未绑定角色'
}

// ─── 棋盘格参数 ───────────────────────────────────────────────────────────────

const boardConfig = reactive<CalibBoardConfigDto>({
    boardType: CalibrationBoardType.Chessboard,
    physicalCornerRows: 9,
    physicalCornerCols: 6,
    physicalSquareSizeMm: 30,
    projectedCornerRows: 9,
    projectedCornerCols: 6,
    projectedPixelSize: 20,
    boardThicknessMm: 1,
    circleBoardConfig: {
        patternSize: { width: 27, height: 27 },
        circleSpacing: 10,
        circleDiameter: 3,
        hasCenterMarker: true,
        hasCornerLocators: false,
        markerPosition: { row: 13, col: 13 },
        detector: {
            minThreshold: 10,
            maxThreshold: 220,
            minArea: 25,
            maxArea: 10000,
            minCircularity: 0.6,
            minConvexity: 0.8,
        },
    },
})

const showDetectorPanel = ref(false)

const boardTypeOptions = computed(() => [
    { label: t('calib.step5BoardTypeChessboard'), value: CalibrationBoardType.Chessboard },
    { label: t('calib.step5BoardTypeSymmetricCircleGrid'), value: CalibrationBoardType.SymmetricCircleGrid },
    { label: t('calib.step5BoardTypeAsymmetricCircleGrid'), value: CalibrationBoardType.AsymmetricCircleGrid },
    {
        label: t('calib.step5BoardTypeMarkedSymmetricCircleGrid'),
        value: CalibrationBoardType.MarkedSymmetricCircleGrid,
    },
])

const isCircleBoard = computed(() => boardConfig.boardType !== CalibrationBoardType.Chessboard)
const isMarkedCircleBoard = computed(() => boardConfig.boardType === CalibrationBoardType.MarkedSymmetricCircleGrid)

function normalizeBoardConfigNumericFields(): void {
    const readNumber = (value: unknown, fallback: number): number => {
        if (typeof value !== 'number' || Number.isNaN(value) || !Number.isFinite(value)) {
            return fallback
        }
        return value
    }

    boardConfig.physicalCornerRows = Math.max(2, Math.round(readNumber(boardConfig.physicalCornerRows, 9)))
    boardConfig.physicalCornerCols = Math.max(2, Math.round(readNumber(boardConfig.physicalCornerCols, 6)))
    boardConfig.physicalSquareSizeMm = Math.max(0.1, readNumber(boardConfig.physicalSquareSizeMm, 30))
    boardConfig.projectedCornerRows = Math.max(2, Math.round(readNumber(boardConfig.projectedCornerRows, 9)))
    boardConfig.projectedCornerCols = Math.max(2, Math.round(readNumber(boardConfig.projectedCornerCols, 6)))
    boardConfig.projectedPixelSize = Math.max(1, Math.round(readNumber(boardConfig.projectedPixelSize, 20)))
    boardConfig.boardThicknessMm = Math.max(0, readNumber(boardConfig.boardThicknessMm, 1))
}

function applyCirclePreset2727(): void {
    boardConfig.boardType = CalibrationBoardType.MarkedSymmetricCircleGrid
    boardConfig.circleBoardConfig = {
        patternSize: { width: 27, height: 27 },
        circleSpacing: 10,
        circleDiameter: 3,
        hasCenterMarker: true,
        hasCornerLocators: false,
        markerPosition: { row: 13, col: 13 },
        detector: {
            minThreshold: 10,
            maxThreshold: 220,
            minArea: 25,
            maxArea: 10000,
            minCircularity: 0.6,
            minConvexity: 0.8,
        },
    }
}

function resetCircleDetectorConfig(): void {
    if (!boardConfig.circleBoardConfig) return
    boardConfig.circleBoardConfig.detector = {
        minThreshold: 10,
        maxThreshold: 220,
        minArea: 25,
        maxArea: 10000,
        minCircularity: 0.6,
        minConvexity: 0.8,
    }
}

const boardConfigSaving = ref(false)
const boardConfigExporting = ref(false)
const boardConfigImporting = ref(false)
const boardConfigImportRef = ref<HTMLInputElement | null>(null)

async function loadBoardConfig(): Promise<void> {
    try {
        const cfg = await getBoardConfig(props.project.id)
        Object.assign(boardConfig, cfg)
        normalizeBoardConfigNumericFields()
        if (boardConfig.boardType !== CalibrationBoardType.Chessboard && !boardConfig.circleBoardConfig) {
            applyCirclePreset2727()
        }
    } catch {
        // 未配置时忽略错误，使用默认值
    }
}

watch(
    () => boardConfig.boardType,
    (nextType) => {
        if (nextType !== CalibrationBoardType.Chessboard && !boardConfig.circleBoardConfig) {
            applyCirclePreset2727()
        }

        // 标记型圆点板必须启用中心标记，切换类型时强制约束到合法状态。
        if (nextType === CalibrationBoardType.MarkedSymmetricCircleGrid) {
            if (!boardConfig.circleBoardConfig) {
                applyCirclePreset2727()
            } else {
                boardConfig.circleBoardConfig.hasCenterMarker = true
            }
        }
    }
)

function validateBoardConfigBeforeSave(): string | null {
    if (boardConfig.boardType === CalibrationBoardType.Chessboard) {
        return null
    }

    const circle = boardConfig.circleBoardConfig
    if (!circle) {
        return t('calib.step5CircleValidationConfigMissing')
    }

    if (circle.patternSize.width < 2 || circle.patternSize.height < 2) {
        return t('calib.step5CircleValidationPatternSize')
    }
    if (circle.circleSpacing <= 0) {
        return t('calib.step5CircleValidationSpacing')
    }
    if (circle.circleDiameter !== null && circle.circleDiameter !== undefined && circle.circleDiameter <= 0) {
        return t('calib.step5CircleValidationDiameter')
    }

    const markerRequired = boardConfig.boardType === CalibrationBoardType.MarkedSymmetricCircleGrid
    if (markerRequired && !circle.hasCenterMarker) {
        return t('calib.step5CircleValidationMarkerRequired')
    }

    if (circle.hasCenterMarker) {
        const row = circle.markerPosition.row
        const col = circle.markerPosition.col
        if (row < 0 || row >= circle.patternSize.height || col < 0 || col >= circle.patternSize.width) {
            return t('calib.step5CircleValidationMarkerRange')
        }
    }

    const detector = circle.detector
    if (detector.minThreshold >= detector.maxThreshold) {
        return t('calib.step5CircleValidationThresholdRange')
    }
    if (detector.minArea <= 0 || detector.maxArea <= 0 || detector.minArea >= detector.maxArea) {
        return t('calib.step5CircleValidationAreaRange')
    }
    if (detector.minCircularity < 0 || detector.minCircularity > 1) {
        return t('calib.step5CircleValidationMinCircularity')
    }
    if (detector.minConvexity < 0 || detector.minConvexity > 1) {
        return t('calib.step5CircleValidationMinConvexity')
    }

    return null
}

async function saveBoardConfig(): Promise<void> {
    normalizeBoardConfigNumericFields()

    const validationError = validateBoardConfigBeforeSave()
    if (validationError) {
        toast.error(validationError)
        return
    }

    boardConfigSaving.value = true
    try {
        await updateBoardConfig({ calibProjectId: props.project.id, ...boardConfig })
        toast.success(t('calib.step5SaveBoardConfigSuccess'))
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        boardConfigSaving.value = false
    }
}

function triggerBoardConfigImport(): void {
    boardConfigImportRef.value?.click()
}

async function onBoardConfigFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file) {
        return
    }

    boardConfigImporting.value = true
    try {
        const cfg = await importBoardConfig(props.project.id, file)
        Object.assign(boardConfig, cfg)
        normalizeBoardConfigNumericFields()
        if (boardConfig.boardType !== CalibrationBoardType.Chessboard && !boardConfig.circleBoardConfig) {
            applyCirclePreset2727()
        }
        toast.success(t('calib.step5ImportBoardConfigSuccess'))
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        boardConfigImporting.value = false
        input.value = ''
    }
}

async function exportCurrentBoardConfig(): Promise<void> {
    boardConfigExporting.value = true
    try {
        const blob = await exportBoardConfig(props.project.id)
        const downloadUrl = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = downloadUrl
        a.download = `calib-board-config-${props.project.id}.json`
        document.body.appendChild(a)
        a.click()
        a.remove()
        URL.revokeObjectURL(downloadUrl)
        toast.success(t('calib.step5ExportBoardConfigSuccess'))
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        boardConfigExporting.value = false
    }
}

// ─── 相机列表 ─────────────────────────────────────────────────────────────────

const cameras = ref<CameraDeviceDto[]>([])
const loading = ref(false)
const expandedCameraId = ref<string | null>(null)

async function loadCameras(): Promise<void> {
    loading.value = true
    try {
        const boundIds = new Set<string>(
            [props.project.mainCameraDeviceId, props.project.secondaryCameraDeviceId].filter(
                (id): id is string => id !== null && id !== undefined
            )
        )
        const result = await getCameraList({ maxResultCount: 50, skipCount: 0 })
        cameras.value = result.items.filter((c) => c.isEnabled && boundIds.has(c.id))
    } finally {
        loading.value = false
    }
}

function toggleCameraExpand(id: string): void {
    expandedCameraId.value = expandedCameraId.value === id ? null : id
    if (expandedCameraId.value === id) {
        void loadCameraStatus(id)
        void loadPhotos(id, CalibPhotoType.Intrinsic)
        void loadPhotos(id, CalibPhotoType.Extrinsic)
        if (isStereoProject.value) {
            void loadPhotos(id, CalibPhotoType.StereoExtrinsicPair)
        }
    }
}

function cameraStatusColor(status: CameraStatus): string {
    switch (status) {
        case CameraStatus.Ready:
            return 'text-green-400'
        case CameraStatus.Capturing:
            return 'text-blue-400'
        case CameraStatus.Error:
            return 'text-red-400'
        default:
            return 'text-muted-foreground/50'
    }
}

function cameraStatusLabel(status: CameraStatus): string {
    switch (status) {
        case CameraStatus.Ready:
            return t('calib.step2Ready')
        case CameraStatus.Capturing:
            return t('calib.step2Capturing')
        case CameraStatus.Error:
            return t('calib.step2Error')
        default:
            return t('calib.step2Closed')
    }
}

// ─── 相机状态（照片计数 + 最新标定结果）────────────────────────────────────────

const cameraStatusMap = ref<Record<string, CalibCameraStatusDto>>({})

async function loadCameraStatus(cameraId: string): Promise<void> {
    try {
        const s = await getCameraStatus(props.project.id, cameraId)
        cameraStatusMap.value[cameraId] = s
    } catch {
        // ignore
    }
}

// ─── 照片列表 ─────────────────────────────────────────────────────────────────

const intrinsicPhotosMap = ref<Record<string, CalibPhotoDto[]>>({})
const extrinsicPhotosMap = ref<Record<string, CalibPhotoDto[]>>({})
const stereoPairPhotosMap = ref<Record<string, CalibPhotoDto[]>>({})

interface CalibExtrinsicSampleViewDto extends CalibExtrinsicSampleDto {
    /** 本组条纹总帧数（off + on 序列） */
    stripeFrameCount: number
    projectorOnPhotos: CalibPhotoDto[]
    invalidFrameCount: number
}

interface ExtrinsicSampleGroupBuilder {
    pairGroupId: string
    projectorOffPhoto?: CalibPhotoDto
    projectorOnPhoto?: CalibPhotoDto
    projectorOnPhotos: CalibPhotoDto[]
    projectorOnCount: number
}

function getExtrinsicSamples(cameraId: string): CalibExtrinsicSampleViewDto[] {
    const photos = extrinsicPhotosMap.value[cameraId] ?? []
    const groups = new Map<string, ExtrinsicSampleGroupBuilder>()

    for (const photo of photos) {
        if (!photo.pairGroupId || photo.extrinsicPhase === null) {
            continue
        }

        const current = groups.get(photo.pairGroupId) ?? {
            pairGroupId: photo.pairGroupId,
            projectorOnPhotos: [],
            projectorOnCount: 0,
        }
        if (
            photo.extrinsicPhase === ExtrinsicPhotoPhase.ProjectorOff ||
            photo.extrinsicPhase === ExtrinsicPhotoPhase.WhiteScreen
        ) {
            current.projectorOffPhoto = photo
        } else if (
            photo.extrinsicPhase === ExtrinsicPhotoPhase.ProjectorOn ||
            photo.extrinsicPhase === ExtrinsicPhotoPhase.Checkerboard
        ) {
            current.projectorOnPhoto = photo
            current.projectorOnPhotos.push(photo)
            current.projectorOnCount += 1
        }
        groups.set(photo.pairGroupId, current)
    }

    return Array.from(groups.values())
        .filter(
            (
                sample
            ): sample is ExtrinsicSampleGroupBuilder & {
                projectorOffPhoto: CalibPhotoDto
                projectorOnPhoto: CalibPhotoDto
            } => !!sample.projectorOffPhoto && !!sample.projectorOnPhoto
        )
        .map((sample) => ({
            pairGroupId: sample.pairGroupId,
            projectorOffPhoto: sample.projectorOffPhoto,
            projectorOnPhoto: sample.projectorOnPhoto,
            isValid:
                sample.projectorOffPhoto.isValid &&
                sample.projectorOnPhotos.every((photo) => photo.isValid) &&
                (sample.projectorOffPhoto.imageDiffSignificant ?? true),
            stripeFrameCount: sample.projectorOnCount + 1,
            projectorOnPhotos: sample.projectorOnPhotos,
            invalidFrameCount:
                (sample.projectorOffPhoto.isValid ? 0 : 1) +
                sample.projectorOnPhotos.filter((photo) => !photo.isValid).length,
        }))
        .sort(
            (a, b) =>
                new Date(b.projectorOffPhoto.capturedAt).getTime() - new Date(a.projectorOffPhoto.capturedAt).getTime()
        )
}

async function loadPhotos(cameraId: string, type: CalibPhotoType): Promise<void> {
    try {
        const photos = await getPhotoList(props.project.id, cameraId, type)
        if (type === CalibPhotoType.Intrinsic) {
            intrinsicPhotosMap.value[cameraId] = photos
        } else if (type === CalibPhotoType.Extrinsic) {
            extrinsicPhotosMap.value[cameraId] = photos
        } else {
            stereoPairPhotosMap.value[cameraId] = photos
        }
    } catch {
        // ignore
    }
}

// ─── 投影仪 LED 控制 ──────────────────────────────────────────────────────────

const ledBusy = ref(false)
const ledIsOn = ref(false)

async function toggleLed(): Promise<void> {
    if (!activeProjectorId.value) return
    ledBusy.value = true
    try {
        if (ledIsOn.value) {
            await ledOff(activeProjectorId.value)
            ledIsOn.value = false
        } else {
            await ledOn(activeProjectorId.value)
            ledIsOn.value = true
        }
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        ledBusy.value = false
    }
}

// ─── 拍照 ─────────────────────────────────────────────────────────────────────

const takingIntrinsicIds = ref<Set<string>>(new Set())
const takingExtrinsicDotIds = ref<Set<string>>(new Set())
const takingExtrinsicCheckerboardIds = ref<Set<string>>(new Set())
const extrinsicStripeImageCount = ref<number>(2)

async function doTakeIntrinsic(cam: CameraDeviceDto): Promise<void> {
    if (takingIntrinsicIds.value.has(cam.id)) return
    takingIntrinsicIds.value.add(cam.id)
    try {
        const photo = await takeIntrinsicPhoto({
            calibProjectId: props.project.id,
            cameraDeviceId: cam.id,
            // 如果有投影仪，将其 ID 一并传入，后端拍照前自动关灯
            projectorDeviceId: activeProjectorId.value ?? undefined,
        })
        // 追加到列表头部（最新的在前）
        const list = intrinsicPhotosMap.value[cam.id] ?? []
        intrinsicPhotosMap.value[cam.id] = [photo, ...list]
        // 刷新计数
        await loadCameraStatus(cam.id)
        if (!photo.isValid) {
            toast.warn(`${cam.name}: 未检测到棋盘格角点，该照片标记为无效`)
        }
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        takingIntrinsicIds.value.delete(cam.id)
    }
}

async function doTakeExtrinsicDot(cam: CameraDeviceDto): Promise<void> {
    if (!canUseProjectorExtrinsic(cam.id)) {
        toast.warn('当前相机不需要执行投影外参拍照')
        return
    }

    if (!activeProjectorId.value) {
        toast.warn(t('calib.step5NoProjectorWarning'))
        return
    }
    if (takingExtrinsicDotIds.value.has(cam.id)) return
    takingExtrinsicDotIds.value.add(cam.id)
    try {
        const photo = await takeExtrinsicDotPhoto({
            calibProjectId: props.project.id,
            cameraDeviceId: cam.id,
        })
        const list = extrinsicPhotosMap.value[cam.id] ?? []
        extrinsicPhotosMap.value[cam.id] = [photo, ...list]
        await loadCameraStatus(cam.id)
        if (!photo.isValid) {
            toast.warn(`${cam.name}: 未检测到圆点标定板角点，该照片标记为无效`)
        } else {
            toast.success(`${cam.name}: 圆点标定板拍照成功，请移除标定板后继续拍摄棋盘格`)
        }
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        takingExtrinsicDotIds.value.delete(cam.id)
    }
}

async function doTakeExtrinsicCheckerboard(cam: CameraDeviceDto): Promise<void> {
    if (!canUseProjectorExtrinsic(cam.id)) {
        toast.warn('当前相机不需要执行投影外参拍照')
        return
    }

    if (!activeProjectorId.value) {
        toast.warn(t('calib.step5NoProjectorWarning'))
        return
    }
    if (takingExtrinsicCheckerboardIds.value.has(cam.id)) return
    takingExtrinsicCheckerboardIds.value.add(cam.id)
    try {
        const photo = await takeExtrinsicCheckerboardPhoto({
            calibProjectId: props.project.id,
            cameraDeviceId: cam.id,
        })
        const list = extrinsicPhotosMap.value[cam.id] ?? []
        extrinsicPhotosMap.value[cam.id] = [photo, ...list]
        await loadCameraStatus(cam.id)
        if (!photo.isValid) {
            toast.warn(`${cam.name}: 未检测到棋盘格角点，该照片标记为无效`)
        }
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        takingExtrinsicCheckerboardIds.value.delete(cam.id)
    }
}

// ─── 计算内外参 ───────────────────────────────────────────────────────────────

const MIN_VALID = 15
const MIN_STEREO_PAIR_VALID = 15
const MAX_SINGLE_REPROJ_ERROR = 0.08
const MAX_STEREO_REPROJ_ERROR = 0.1
const calibResultMap = ref<Record<string, CalibComputeResultDto | null>>({})
const stereoStatus = ref<CalibStereoStatusDto | null>(null)
const stereoComputing = ref(false)
const stereoTaking = ref(false)
const stereoResult = ref<CalibStereoComputeResultDto | null>(null)

const canTakeStereoPair = computed(() => {
    return isStereoProject.value && !!props.project.mainCameraDeviceId && !!props.project.secondaryCameraDeviceId
})

const canComputeStereo = computed(() => {
    return (stereoStatus.value?.pairValid ?? 0) >= MIN_STEREO_PAIR_VALID
})

async function loadStereoStatusData(): Promise<void> {
    if (!isStereoProject.value) return
    try {
        const s = await getStereoStatus(props.project.id)
        stereoStatus.value = s
        stereoResult.value = s.latestResult
    } catch {
        // ignore
    }
}

function canCompute(cameraId: string): boolean {
    const s = cameraStatusMap.value[cameraId]
    return !!s && s.intrinsicValid >= MIN_VALID
}

/** 判断是否可计算外参：已有内参 + 有足够外参照片 */
function canComputeExtrinsic(cameraId: string): boolean {
    if (!canUseProjectorExtrinsic(cameraId)) return false
    const s = cameraStatusMap.value[cameraId]
    const result = calibResultMap.value[cameraId]
    return !!s && s.extrinsicValid >= 1 && (!!result?.intrinsicMatrixJson || !!s.intrinsicValid)
}

const intrinsicComputingIds = ref(new Set<string>())
const extrinsicComputingIds = ref(new Set<string>())

async function doComputeIntrinsic(cam: CameraDeviceDto): Promise<void> {
    if (!canCompute(cam.id) || intrinsicComputingIds.value.has(cam.id)) return
    intrinsicComputingIds.value.add(cam.id)
    try {
        const result = await computeIntrinsic(props.project.id, cam.id)
        calibResultMap.value[cam.id] = result
        await loadCameraStatus(cam.id)
        toast.success(t('calib.step5ComputeIntrinsicSuccess', { error: result.reprojectionError.toFixed(3) }))
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        intrinsicComputingIds.value.delete(cam.id)
    }
}

async function doComputeExtrinsic(cam: CameraDeviceDto): Promise<void> {
    if (!canComputeExtrinsic(cam.id) || extrinsicComputingIds.value.has(cam.id)) return
    extrinsicComputingIds.value.add(cam.id)
    try {
        const result = await computeExtrinsic(props.project.id, cam.id)
        calibResultMap.value[cam.id] = result
        await loadCameraStatus(cam.id)
        toast.success(t('calib.step5ComputeExtrinsicSuccess'))
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        extrinsicComputingIds.value.delete(cam.id)
    }
}

async function doTakeStereoPair(): Promise<void> {
    if (!canTakeStereoPair.value || stereoTaking.value) return
    stereoTaking.value = true
    try {
        const pair = await takeStereoExtrinsicPairPhoto({ calibProjectId: props.project.id })
        const mainCameraId = props.project.mainCameraDeviceId as string
        const secondaryCameraId = props.project.secondaryCameraDeviceId as string

        stereoPairPhotosMap.value[mainCameraId] = [pair.mainPhoto, ...(stereoPairPhotosMap.value[mainCameraId] ?? [])]
        stereoPairPhotosMap.value[secondaryCameraId] = [
            pair.secondaryPhoto,
            ...(stereoPairPhotosMap.value[secondaryCameraId] ?? []),
        ]

        await Promise.all([loadCameraStatus(mainCameraId), loadCameraStatus(secondaryCameraId), loadStereoStatusData()])

        if (!pair.mainPhoto.isValid || !pair.secondaryPhoto.isValid) {
            toast.warn('本组成对照片存在无效样本，请补拍')
        }
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        stereoTaking.value = false
    }
}

async function doComputeStereo(): Promise<void> {
    if (!canComputeStereo.value || stereoComputing.value) return
    stereoComputing.value = true
    try {
        const result = await computeStereoCalibration(props.project.id)
        stereoResult.value = result
        await loadStereoStatusData()
        toast.success(`双目联合计算完成，误差 ${result.stereoReprojectionError.toFixed(3)} px`)
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
    } finally {
        stereoComputing.value = false
    }
}

// ─── 删除照片 ─────────────────────────────────────────────────────────────────

async function doDeletePhoto(photo: CalibPhotoDto, cameraId: string): Promise<void> {
    confirm.require({
        message: t('calib.step5DeleteConfirm'),
        accept: async () => {
            try {
                await deletePhoto(photo.id)
                // 从列表移除
                if (photo.photoType === CalibPhotoType.Intrinsic) {
                    intrinsicPhotosMap.value[cameraId] = (intrinsicPhotosMap.value[cameraId] ?? []).filter(
                        (p) => p.id !== photo.id
                    )
                } else if (photo.photoType === CalibPhotoType.Extrinsic) {
                    extrinsicPhotosMap.value[cameraId] = (extrinsicPhotosMap.value[cameraId] ?? []).filter((p) => {
                        if (photo.pairGroupId) {
                            return p.pairGroupId !== photo.pairGroupId
                        }
                        return p.id !== photo.id
                    })
                } else {
                    stereoPairPhotosMap.value[cameraId] = (stereoPairPhotosMap.value[cameraId] ?? []).filter(
                        (p) => p.id !== photo.id
                    )
                }
                await loadCameraStatus(cameraId)
                await loadStereoStatusData()
                toast.success(t('calib.step5PhotoDeleteSuccess'))
            } catch (e: unknown) {
                toast.error(e instanceof Error ? e.message : String(e))
            }
        },
    })
}

const deletingInvalidIds = ref<Set<string>>(new Set())

async function doDeleteInvalidPhotos(cameraId: string): Promise<void> {
    const status = cameraStatusMap.value[cameraId]
    const invalidCount =
        (status?.intrinsicTotal ?? 0) -
        (status?.intrinsicValid ?? 0) +
        (status?.extrinsicTotal ?? 0) -
        (status?.extrinsicValid ?? 0) +
        (stereoPairPhotosMap.value[cameraId]?.filter((p) => !p.isValid).length ?? 0)
    if (invalidCount === 0) {
        toast.warn(t('calib.step5NoInvalidPhotos'))
        return
    }
    confirm.require({
        message: t('calib.step5DeleteInvalidConfirm', { count: invalidCount }),
        accept: async () => {
            deletingInvalidIds.value.add(cameraId)
            try {
                const deleted = await deleteInvalidPhotos(props.project.id, cameraId)
                // 从本地列表移除无效照片
                if (intrinsicPhotosMap.value[cameraId]) {
                    intrinsicPhotosMap.value[cameraId] = intrinsicPhotosMap.value[cameraId].filter((p) => p.isValid)
                }
                await loadPhotos(cameraId, CalibPhotoType.Extrinsic)
                if (stereoPairPhotosMap.value[cameraId]) {
                    stereoPairPhotosMap.value[cameraId] = stereoPairPhotosMap.value[cameraId].filter((p) => p.isValid)
                }
                await loadCameraStatus(cameraId)
                await loadStereoStatusData()
                toast.success(t('calib.step5DeleteInvalidSuccess', { count: deleted }))
            } catch (e: unknown) {
                toast.error(e instanceof Error ? e.message : String(e))
            } finally {
                deletingInvalidIds.value.delete(cameraId)
            }
        },
    })
}

// ─── 解析内参矩阵为可读字符串 ─────────────────────────────────────────────────

function formatMatrix(json: string | null | undefined): string {
    if (!json) return '—'
    try {
        const arr = JSON.parse(json) as number[][]
        return arr.map((row) => row.map((v) => v.toFixed(2)).join('  ')).join('\n')
    } catch {
        return json
    }
}

function formatVec(json: string | null | undefined): string {
    if (!json) return '—'
    try {
        const arr = JSON.parse(json) as number[]
        return arr.map((v) => v.toFixed(4)).join(', ')
    } catch {
        return json
    }
}

// ─── 生命周期 ─────────────────────────────────────────────────────────────────

onMounted(async () => {
    await Promise.all([loadBoardConfig(), loadCameras(), loadStereoStatusData()])
})
</script>

<template>
    <ConfirmDialog />
    <input
        ref="boardConfigImportRef"
        type="file"
        accept=".json,application/json"
        class="hidden"
        @change="onBoardConfigFileSelected"
    />

    <div class="flex flex-1 min-h-0 flex-col gap-3 overflow-y-auto p-4">
        <!-- ── 顶部：标定板参数配置 ───────────────────────────────────── -->
        <div class="rounded-xl border border-border/40 bg-muted/10 px-4 py-4">
            <div class="mb-3 flex items-center justify-between gap-2">
                <h3 class="text-sm font-semibold text-foreground">
                    {{ t('calib.step5BoardConfigTitle') }}
                </h3>
                <!-- 图像追踪（暂未开放）-->
                <Tag
                    :value="t('calib.step5ImageTracking') + '：' + t('calib.step5ImageTrackingNotAvailable')"
                    severity="secondary"
                    class="!text-xs"
                />
            </div>

            <div class="mb-3 grid grid-cols-1 gap-3 md:grid-cols-3">
                <div class="md:col-span-2">
                    <label class="mb-1 block text-xs text-muted-foreground">{{ t('calib.step5BoardTypeLabel') }}</label>
                    <Select
                        v-model="boardConfig.boardType"
                        :options="boardTypeOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="w-full"
                    />
                </div>
                <div class="flex items-end">
                    <Button size="small" severity="secondary" outlined class="!text-xs" @click="applyCirclePreset2727">
                        {{ t('calib.step5PresetMarked2727') }}
                    </Button>
                </div>
            </div>

            <div class="grid grid-cols-1 gap-8 xl:grid-cols-2">
                <!-- 实体标定板（棋盘格或圆点）-->
                <div v-if="!isCircleBoard">
                    <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                        {{ t('calib.step5PhysicalBoard') }}
                    </p>
                    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 2xl:grid-cols-3">
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CornerRows') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.physicalCornerRows"
                                size="small"
                                :use-grouping="false"
                                :min="2"
                                :max="100"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CornerCols') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.physicalCornerCols"
                                size="small"
                                :use-grouping="false"
                                :min="2"
                                :max="100"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5SquareSizeMm') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.physicalSquareSizeMm"
                                size="small"
                                :use-grouping="false"
                                :min="0.1"
                                :max-fraction-digits="2"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                    </div>
                </div>

                <!-- 投影棋盘格（独立显示，不受标定板类型影响）-->
                <div v-if="showProjectorSection">
                    <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                        {{ t('calib.step5ProjectedBoard') }}
                    </p>
                    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 2xl:grid-cols-3">
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CornerRows') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.projectedCornerRows"
                                size="small"
                                :use-grouping="false"
                                :min="2"
                                :max="100"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CornerCols') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.projectedCornerCols"
                                size="small"
                                :use-grouping="false"
                                :min="2"
                                :max="100"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5ProjectedPixelSize') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.projectedPixelSize"
                                size="small"
                                :use-grouping="false"
                                :min="1"
                                :max="4096"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                    </div>

                    <div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5BoardThicknessMm') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.boardThicknessMm"
                                size="small"
                                :use-grouping="false"
                                :min="0"
                                :max="100"
                                :max-fraction-digits="3"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                    </div>
                </div>

                <!-- 圆点标定板 -->
                <div v-if="isCircleBoard" class="xl:col-span-2">
                    <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                        {{ t('calib.step5CircleBoardSectionTitle') }}
                    </p>
                    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 2xl:grid-cols-4">
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CirclePatternWidth') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.circleBoardConfig!.patternSize.width"
                                size="small"
                                :use-grouping="false"
                                :min="2"
                                :max="200"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CirclePatternHeight') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.circleBoardConfig!.patternSize.height"
                                size="small"
                                :use-grouping="false"
                                :min="2"
                                :max="200"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CircleSpacingMm') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.circleBoardConfig!.circleSpacing"
                                size="small"
                                :use-grouping="false"
                                :min="0.01"
                                :max-fraction-digits="3"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div class="min-w-0">
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CircleDiameterMm') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.circleBoardConfig!.circleDiameter"
                                size="small"
                                :use-grouping="false"
                                :min="0"
                                :max-fraction-digits="3"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                    </div>

                    <div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
                        <div class="flex items-center gap-2 text-xs text-muted-foreground">
                            <Checkbox v-model="boardConfig.circleBoardConfig!.hasCenterMarker" binary />
                            <span>{{ t('calib.step5CircleCenterMarker') }}</span>
                        </div>
                        <div class="flex items-center gap-2 text-xs text-muted-foreground">
                            <Checkbox v-model="boardConfig.circleBoardConfig!.hasCornerLocators" binary />
                            <span>{{ t('calib.step5CircleCornerLocators') }}</span>
                        </div>
                        <div
                            class="min-w-0"
                            v-if="isMarkedCircleBoard || boardConfig.circleBoardConfig!.hasCenterMarker"
                        >
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CircleMarkerRow') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.circleBoardConfig!.markerPosition.row"
                                size="small"
                                :use-grouping="false"
                                :min="0"
                                :max="boardConfig.circleBoardConfig!.patternSize.height - 1"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                        <div
                            class="min-w-0"
                            v-if="isMarkedCircleBoard || boardConfig.circleBoardConfig!.hasCenterMarker"
                        >
                            <label class="mb-1 block text-xs text-muted-foreground">
                                {{ t('calib.step5CircleMarkerCol') }}
                            </label>
                            <InputNumber
                                v-model="boardConfig.circleBoardConfig!.markerPosition.col"
                                size="small"
                                :use-grouping="false"
                                :min="0"
                                :max="boardConfig.circleBoardConfig!.patternSize.width - 1"
                                class="w-full"
                                :input-class="'!text-xs !h-7 !py-0'"
                            />
                        </div>
                    </div>

                    <div class="mt-3 rounded-lg border border-border/40 bg-muted/10 p-3">
                        <div class="mb-2 flex items-center justify-between">
                            <p class="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                                {{ t('calib.step5CircleDetectorTitle') }}
                            </p>
                            <div class="flex items-center gap-2">
                                <Button
                                    size="small"
                                    text
                                    class="!text-xs"
                                    @click="showDetectorPanel = !showDetectorPanel"
                                >
                                    {{ showDetectorPanel ? t('calib.step5Collapse') : t('calib.step5Expand') }}
                                </Button>
                                <Button
                                    size="small"
                                    severity="secondary"
                                    outlined
                                    class="!text-xs"
                                    @click="resetCircleDetectorConfig"
                                >
                                    {{ t('calib.step5ResetDefaults') }}
                                </Button>
                            </div>
                        </div>

                        <div v-if="showDetectorPanel" class="grid grid-cols-1 gap-3 sm:grid-cols-3">
                            <div class="min-w-0">
                                <label class="mb-1 block text-xs text-muted-foreground">
                                    {{ t('calib.step5DetectorMinThreshold') }}
                                </label>
                                <InputNumber
                                    v-model="boardConfig.circleBoardConfig!.detector.minThreshold"
                                    size="small"
                                    :use-grouping="false"
                                    class="w-full"
                                    :input-class="'!text-xs !h-7 !py-0'"
                                />
                            </div>
                            <div class="min-w-0">
                                <label class="mb-1 block text-xs text-muted-foreground">
                                    {{ t('calib.step5DetectorMaxThreshold') }}
                                </label>
                                <InputNumber
                                    v-model="boardConfig.circleBoardConfig!.detector.maxThreshold"
                                    size="small"
                                    :use-grouping="false"
                                    class="w-full"
                                    :input-class="'!text-xs !h-7 !py-0'"
                                />
                            </div>
                            <div class="min-w-0">
                                <label class="mb-1 block text-xs text-muted-foreground">
                                    {{ t('calib.step5DetectorMinArea') }}
                                </label>
                                <InputNumber
                                    v-model="boardConfig.circleBoardConfig!.detector.minArea"
                                    size="small"
                                    :use-grouping="false"
                                    class="w-full"
                                    :input-class="'!text-xs !h-7 !py-0'"
                                />
                            </div>
                            <div class="min-w-0">
                                <label class="mb-1 block text-xs text-muted-foreground">
                                    {{ t('calib.step5DetectorMaxArea') }}
                                </label>
                                <InputNumber
                                    v-model="boardConfig.circleBoardConfig!.detector.maxArea"
                                    size="small"
                                    :use-grouping="false"
                                    class="w-full"
                                    :input-class="'!text-xs !h-7 !py-0'"
                                />
                            </div>
                            <div class="min-w-0">
                                <label class="mb-1 block text-xs text-muted-foreground">
                                    {{ t('calib.step5DetectorMinCircularity') }}
                                </label>
                                <InputNumber
                                    v-model="boardConfig.circleBoardConfig!.detector.minCircularity"
                                    size="small"
                                    :use-grouping="false"
                                    :min="0"
                                    :max="1"
                                    :max-fraction-digits="3"
                                    class="w-full"
                                    :input-class="'!text-xs !h-7 !py-0'"
                                />
                            </div>
                            <div class="min-w-0">
                                <label class="mb-1 block text-xs text-muted-foreground">
                                    {{ t('calib.step5DetectorMinConvexity') }}
                                </label>
                                <InputNumber
                                    v-model="boardConfig.circleBoardConfig!.detector.minConvexity"
                                    size="small"
                                    :use-grouping="false"
                                    :min="0"
                                    :max="1"
                                    :max-fraction-digits="3"
                                    class="w-full"
                                    :input-class="'!text-xs !h-7 !py-0'"
                                />
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <div class="mt-3 flex items-center justify-end gap-2">
                <div class="flex items-center gap-2">
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        class="!text-xs"
                        :loading="boardConfigExporting"
                        @click="exportCurrentBoardConfig"
                    >
                        <Download class="mr-1.5 size-3" />
                        {{ t('calib.step5ExportBoardConfig') }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        class="!text-xs"
                        :loading="boardConfigImporting"
                        @click="triggerBoardConfigImport"
                    >
                        <Upload class="mr-1.5 size-3" />
                        {{ t('calib.step5ImportBoardConfig') }}
                    </Button>
                    <Button size="small" class="!text-xs" :loading="boardConfigSaving" @click="saveBoardConfig">
                        {{ t('calib.step5SaveBoardConfig') }}
                    </Button>
                </div>
            </div>
        </div>

        <!-- ── 相机列表 ────────────────────────────────────────────────── -->

        <div class="flex items-center justify-between gap-2">
            <span class="text-sm font-medium text-foreground">{{ t('calib.step5CameraList') }}</span>
            <Button severity="secondary" outlined size="small" :loading="loading" class="!text-xs" @click="loadCameras">
                <RefreshCcw class="mr-1.5 size-3" />
                {{ t('calib.step5Refresh') }}
            </Button>
        </div>

        <div
            v-if="isStereoProject"
            class="rounded-lg border border-blue-500/30 bg-blue-500/5 px-3 py-2 text-xs text-blue-100"
        >
            双目流程引导：先完成主/从相机内参采集与计算，再进行双目成对拍照（至少
            {{ MIN_STEREO_PAIR_VALID }} 组），最后执行双目联合计算。
            <span class="ml-1 text-blue-300">
                当前有效组数：{{ stereoStatus?.pairValid ?? 0 }}/{{ stereoStatus?.pairTotal ?? 0 }}
            </span>
        </div>

        <div v-if="loading" class="flex items-center justify-center gap-2 py-10 text-sm text-muted-foreground">
            <Loader2 class="size-4 animate-spin" />
            {{ t('common.loading') }}
        </div>

        <div v-else-if="cameras.length === 0" class="py-10 text-center text-sm text-muted-foreground">
            {{ t('calib.step5NoCamera') }}
        </div>

        <div v-else class="flex flex-col gap-2">
            <div v-for="cam in cameras" :key="cam.id" class="overflow-hidden rounded-lg border border-border/50">
                <!-- 相机卡片头部（可折叠） -->
                <button
                    class="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-muted/20"
                    :class="{ 'bg-muted/20': expandedCameraId === cam.id }"
                    @click="toggleCameraExpand(cam.id)"
                >
                    <Camera
                        :class="['size-4 shrink-0', cam.isEnabled ? 'text-blue-400' : 'text-muted-foreground/40']"
                    />
                    <div class="min-w-0 flex-1">
                        <div class="flex items-center gap-2">
                            <div class="truncate text-sm font-medium">{{ cam.name }}</div>
                            <Tag :value="cameraRoleLabel(cam.id)" severity="secondary" class="!text-[10px]" />
                        </div>
                        <div v-if="cam.model" class="truncate text-xs text-muted-foreground">
                            {{ cam.model }}
                        </div>
                    </div>

                    <!-- 照片计数摘要 -->
                    <template v-if="cameraStatusMap[cam.id]">
                        <span class="text-xs text-muted-foreground">
                            {{
                                t('calib.step5ValidCount', {
                                    count: cameraStatusMap[cam.id].intrinsicValid,
                                    total: cameraStatusMap[cam.id].intrinsicTotal,
                                })
                            }}
                        </span>
                    </template>

                    <span :class="['text-xs', cameraStatusColor(cam.status)]">
                        ● {{ cameraStatusLabel(cam.status) }}
                    </span>

                    <ChevronDown v-if="expandedCameraId === cam.id" class="size-4 shrink-0 text-muted-foreground" />
                    <ChevronRight v-else class="size-4 shrink-0 text-muted-foreground" />
                </button>

                <!-- 展开内容 -->
                <div v-if="expandedCameraId === cam.id" class="border-t border-border/40 bg-background/20">
                    <div class="grid grid-cols-1 gap-4 p-4 lg:grid-cols-3">
                        <!-- ─ 左栏：控制面板 ── -->
                        <div class="flex flex-col gap-3">
                            <!-- 投影仪 LED 控制 -->
                            <div
                                v-if="canUseProjectorExtrinsic(cam.id)"
                                class="rounded-lg border border-border/30 bg-muted/10 p-3"
                            >
                                <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                                    {{ t('calib.step5ProjectorControl') }}
                                </p>
                                <div class="flex items-center gap-2">
                                    <div
                                        :class="[
                                            'size-2 rounded-full',
                                            ledIsOn ? 'bg-yellow-400' : 'bg-muted-foreground/30',
                                        ]"
                                    />
                                    <span class="text-xs text-muted-foreground">
                                        {{ t('calib.step5LedStatus') }}：
                                        {{ ledIsOn ? t('calib.step5LedOn') : t('calib.step5LedOff') }}
                                    </span>
                                    <Button
                                        :severity="ledIsOn ? 'warn' : 'secondary'"
                                        outlined
                                        size="small"
                                        class="ml-auto !text-xs"
                                        :loading="ledBusy"
                                        @click="toggleLed"
                                    >
                                        <component :is="ledIsOn ? LightbulbOff : Lightbulb" class="mr-1 size-3" />
                                        {{ ledIsOn ? t('calib.step5LedOff') : t('calib.step5LedOn') }}
                                    </Button>
                                </div>
                            </div>
                            <div
                                v-else
                                class="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-amber-400"
                            >
                                {{ t('calib.step5NoProjectorWarning') }}
                            </div>

                            <div
                                v-if="canUseProjectorExtrinsic(cam.id)"
                                class="rounded-lg border border-border/30 bg-muted/10 p-3"
                            >
                                <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                                    {{ t('calib.step3FringeSettings') }}
                                </p>
                                <div class="grid grid-cols-1 gap-2">
                                    <div>
                                        <p class="mb-1 text-[11px] text-muted-foreground">
                                            {{ t('calib.step3ImageCount') }}
                                        </p>
                                        <InputNumber
                                            v-model="extrinsicStripeImageCount"
                                            :min="2"
                                            :max="128"
                                            :step="1"
                                            show-buttons
                                            button-layout="horizontal"
                                            class="w-full"
                                            size="small"
                                        />
                                    </div>
                                </div>
                            </div>

                            <!-- 拍照按钮组 -->
                            <div class="flex flex-col gap-2">
                                <Button
                                    size="small"
                                    severity="secondary"
                                    class="w-full !text-xs"
                                    :loading="takingIntrinsicIds.has(cam.id)"
                                    @click="doTakeIntrinsic(cam)"
                                >
                                    <Camera class="mr-1.5 size-3" />
                                    {{
                                        takingIntrinsicIds.has(cam.id)
                                            ? t('calib.step5TakingPhoto')
                                            : t('calib.step5TakeIntrinsic')
                                    }}
                                </Button>

                                <Button
                                    size="small"
                                    severity="secondary"
                                    class="w-full !text-xs"
                                    :loading="takingExtrinsicDotIds.has(cam.id)"
                                    :disabled="!canUseProjectorExtrinsic(cam.id)"
                                    @click="doTakeExtrinsicDot(cam)"
                                >
                                    <Zap class="mr-1.5 size-3" />
                                    {{
                                        takingExtrinsicDotIds.has(cam.id)
                                            ? t('calib.step5TakingPhoto')
                                            : t('calib.step5TakeExtrinsicDot')
                                    }}
                                </Button>

                                <Button
                                    size="small"
                                    severity="secondary"
                                    class="w-full !text-xs"
                                    :loading="takingExtrinsicCheckerboardIds.has(cam.id)"
                                    :disabled="!canUseProjectorExtrinsic(cam.id)"
                                    @click="doTakeExtrinsicCheckerboard(cam)"
                                >
                                    <Zap class="mr-1.5 size-3" />
                                    {{
                                        takingExtrinsicCheckerboardIds.has(cam.id)
                                            ? t('calib.step5TakingPhoto')
                                            : t('calib.step5TakeExtrinsicCheckerboard')
                                    }}
                                </Button>

                                <Button
                                    size="small"
                                    class="w-full !text-xs"
                                    :loading="intrinsicComputingIds.has(cam.id)"
                                    :disabled="!canCompute(cam.id)"
                                    :title="
                                        !canCompute(cam.id)
                                            ? t('calib.step5IntrinsicInsufficient', {
                                                  min: MIN_VALID,
                                                  current: cameraStatusMap[cam.id]?.intrinsicValid ?? 0,
                                              })
                                            : ''
                                    "
                                    @click="doComputeIntrinsic(cam)"
                                >
                                    {{
                                        intrinsicComputingIds.has(cam.id)
                                            ? t('calib.step5Computing')
                                            : t('calib.step5ComputeIntrinsic')
                                    }}
                                </Button>

                                <Button
                                    v-if="canUseProjectorExtrinsic(cam.id)"
                                    size="small"
                                    severity="secondary"
                                    class="w-full !text-xs"
                                    :loading="extrinsicComputingIds.has(cam.id)"
                                    :disabled="!canComputeExtrinsic(cam.id)"
                                    :title="!canComputeExtrinsic(cam.id) ? t('calib.step5ExtrinsicInsufficient') : ''"
                                    @click="doComputeExtrinsic(cam)"
                                >
                                    {{
                                        extrinsicComputingIds.has(cam.id)
                                            ? t('calib.step5Computing')
                                            : t('calib.step5ComputeExtrinsic')
                                    }}
                                </Button>
                            </div>

                            <div
                                v-if="isStereoProject && cam.id === props.project.mainCameraDeviceId"
                                class="rounded-lg border border-blue-500/30 bg-blue-500/5 p-3"
                            >
                                <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-blue-300">
                                    双目联合外参
                                </p>
                                <p class="text-xs text-muted-foreground">
                                    成对有效样本：{{ stereoStatus?.pairValid ?? 0 }}/{{
                                        stereoStatus?.pairTotal ?? 0
                                    }}， 至少 {{ MIN_STEREO_PAIR_VALID }} 组可计算
                                </p>
                                <div class="mt-2 flex flex-col gap-2">
                                    <Button
                                        size="small"
                                        severity="secondary"
                                        class="w-full !text-xs"
                                        :loading="stereoTaking"
                                        :disabled="!canTakeStereoPair"
                                        @click="doTakeStereoPair"
                                    >
                                        <Camera class="mr-1.5 size-3" />
                                        {{ stereoTaking ? '拍照中...' : '双目成对拍照' }}
                                    </Button>
                                    <Button
                                        size="small"
                                        class="w-full !text-xs"
                                        :loading="stereoComputing"
                                        :disabled="!canComputeStereo"
                                        :title="
                                            !canComputeStereo
                                                ? `双目有效样本不足 ${MIN_STEREO_PAIR_VALID} 组（当前 ${stereoStatus?.pairValid ?? 0}）`
                                                : ''
                                        "
                                        @click="doComputeStereo"
                                    >
                                        {{ stereoComputing ? '计算中...' : '计算双目联合外参' }}
                                    </Button>
                                </div>
                            </div>

                            <!-- 标定结果 -->
                            <div class="rounded-lg border border-border/30 bg-muted/10 p-3">
                                <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                                    {{ t('calib.step5CalibResult') }}
                                </p>
                                <p
                                    v-if="
                                        props.project.deviceType === CalibDeviceType.TwoCamera1Light &&
                                        cam.id !== props.project.mainCameraDeviceId
                                    "
                                    class="mb-2 text-[11px] text-amber-400"
                                >
                                    2目1光模式下仅主相机执行投影外参，从相机与投影仪关系由双目联合结果推导。
                                </p>

                                <template v-if="cameraStatusMap[cam.id]?.latestResult">
                                    <div class="space-y-1.5 text-xs">
                                        <div>
                                            <span class="text-muted-foreground">
                                                {{ t('calib.step5ReprojError') }}：
                                            </span>
                                            <span
                                                class="font-mono"
                                                :class="
                                                    cameraStatusMap[cam.id]!.latestResult!.reprojectionError <=
                                                    MAX_SINGLE_REPROJ_ERROR
                                                        ? 'text-green-400'
                                                        : 'text-red-400'
                                                "
                                            >
                                                {{
                                                    cameraStatusMap[cam.id]!.latestResult!.reprojectionError.toFixed(3)
                                                }}
                                                px
                                            </span>
                                        </div>
                                        <div
                                            v-if="
                                                cameraStatusMap[cam.id]!.latestResult!.projectorReprojectionError !==
                                                null
                                            "
                                        >
                                            <span class="text-muted-foreground">投影外参误差：</span>
                                            <span
                                                class="font-mono"
                                                :class="
                                                    (cameraStatusMap[cam.id]!.latestResult!
                                                        .projectorReprojectionError ?? 0) <= MAX_SINGLE_REPROJ_ERROR
                                                        ? 'text-green-400'
                                                        : 'text-red-400'
                                                "
                                            >
                                                {{
                                                    (
                                                        cameraStatusMap[cam.id]!.latestResult!
                                                            .projectorReprojectionError ?? 0
                                                    ).toFixed(3)
                                                }}
                                                px
                                            </span>
                                        </div>
                                        <div>
                                            <span class="text-muted-foreground">
                                                {{ t('calib.step5IntrinsicMatrix') }}：
                                            </span>
                                            <pre class="mt-1 whitespace-pre font-mono text-[10px] text-foreground/80">{{
                                                formatMatrix(cameraStatusMap[cam.id]!.latestResult!.intrinsicMatrixJson)
                                            }}</pre>
                                        </div>
                                        <div>
                                            <span class="text-muted-foreground">
                                                {{ t('calib.step5DistCoeffs') }}：
                                            </span>
                                            <span class="font-mono text-[10px]">
                                                {{ formatVec(cameraStatusMap[cam.id]!.latestResult!.distCoeffsJson) }}
                                            </span>
                                        </div>
                                        <template v-if="cameraStatusMap[cam.id]!.latestResult!.extrinsicRvecJson">
                                            <div>
                                                <span class="text-muted-foreground">
                                                    {{ t('calib.step5ExtrinsicRvec') }}：
                                                </span>
                                                <span class="font-mono text-[10px]">
                                                    {{
                                                        formatVec(
                                                            cameraStatusMap[cam.id]!.latestResult!.extrinsicRvecJson
                                                        )
                                                    }}
                                                </span>
                                            </div>
                                            <div>
                                                <span class="text-muted-foreground">
                                                    {{ t('calib.step5ExtrinsicTvec') }}：
                                                </span>
                                                <span class="font-mono text-[10px]">
                                                    {{
                                                        formatVec(
                                                            cameraStatusMap[cam.id]!.latestResult!.extrinsicTvecJson
                                                        )
                                                    }}
                                                </span>
                                            </div>
                                        </template>
                                    </div>
                                </template>
                                <p v-else class="text-xs text-muted-foreground/60">
                                    {{ t('calib.step5NoCalibResult') }}
                                </p>

                                <template
                                    v-if="
                                        isStereoProject && cam.id === props.project.mainCameraDeviceId && stereoResult
                                    "
                                >
                                    <div class="mt-3 border-t border-border/40 pt-3 text-xs">
                                        <p class="mb-1 font-semibold text-blue-300">双目联合结果</p>
                                        <div>
                                            <span class="text-muted-foreground">重投影误差：</span>
                                            <span
                                                class="font-mono"
                                                :class="
                                                    stereoResult.stereoReprojectionError <= MAX_STEREO_REPROJ_ERROR
                                                        ? 'text-green-400'
                                                        : 'text-red-400'
                                                "
                                            >
                                                {{ stereoResult.stereoReprojectionError.toFixed(3) }} px
                                            </span>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">矫正 map：</span>
                                            <span class="font-mono text-[10px]">
                                                {{ stereoResult.rectifyMapWidth }}x{{ stereoResult.rectifyMapHeight }}
                                            </span>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">map1x key：</span>
                                            <span class="font-mono text-[10px] break-all">
                                                {{ stereoResult.map1XBlobKey }}
                                            </span>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">map1y key：</span>
                                            <span class="font-mono text-[10px] break-all">
                                                {{ stereoResult.map1YBlobKey }}
                                            </span>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">map2x key：</span>
                                            <span class="font-mono text-[10px] break-all">
                                                {{ stereoResult.map2XBlobKey }}
                                            </span>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">map2y key：</span>
                                            <span class="font-mono text-[10px] break-all">
                                                {{ stereoResult.map2YBlobKey }}
                                            </span>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">R(L→R)：</span>
                                            <pre class="mt-1 whitespace-pre font-mono text-[10px] text-foreground/80">{{
                                                formatMatrix(stereoResult.rotationMatrixJson)
                                            }}</pre>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">t(L→R)：</span>
                                            <span class="font-mono text-[10px]">
                                                {{ formatVec(stereoResult.translationVectorJson) }}
                                            </span>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">T(L→R)：</span>
                                            <pre class="mt-1 whitespace-pre font-mono text-[10px] text-foreground/80">{{
                                                formatMatrix(stereoResult.transformLtoRJson)
                                            }}</pre>
                                        </div>
                                        <div class="mt-1">
                                            <span class="text-muted-foreground">T(R→L)：</span>
                                            <pre class="mt-1 whitespace-pre font-mono text-[10px] text-foreground/80">{{
                                                formatMatrix(stereoResult.transformRtoLJson)
                                            }}</pre>
                                        </div>
                                    </div>
                                </template>
                            </div>
                        </div>

                        <!-- ─ 右栏：照片展示（内参 / 外参 Tab）── -->
                        <div class="lg:col-span-2">
                            <Tabs :value="canUseProjectorExtrinsic(cam.id) ? 'intrinsic' : 'intrinsic-only'">
                                <div class="flex items-center justify-between gap-2">
                                    <TabList>
                                        <Tab value="intrinsic">
                                            {{ t('calib.step5IntrinsicPhotos') }}
                                            <span
                                                v-if="cameraStatusMap[cam.id]"
                                                class="ml-1.5 text-xs text-muted-foreground"
                                            >
                                                ({{ cameraStatusMap[cam.id].intrinsicValid }}/{{
                                                    cameraStatusMap[cam.id].intrinsicTotal
                                                }})
                                            </span>
                                        </Tab>
                                        <Tab v-if="canUseProjectorExtrinsic(cam.id)" value="extrinsic">
                                            {{ t('calib.step5ExtrinsicPhotos') }}
                                            <span
                                                v-if="cameraStatusMap[cam.id]"
                                                class="ml-1.5 text-xs text-muted-foreground"
                                            >
                                                ({{ cameraStatusMap[cam.id].extrinsicValid }}/{{
                                                    cameraStatusMap[cam.id].extrinsicTotal
                                                }})
                                            </span>
                                        </Tab>
                                        <Tab v-if="isStereoProject" value="stereo-pair">
                                            双目成对照片
                                            <span
                                                v-if="stereoPairPhotosMap[cam.id]"
                                                class="ml-1.5 text-xs text-muted-foreground"
                                            >
                                                ({{ stereoPairPhotosMap[cam.id].filter((p) => p.isValid).length }}/{{
                                                    stereoPairPhotosMap[cam.id].length
                                                }})
                                            </span>
                                        </Tab>
                                    </TabList>
                                    <Button
                                        severity="danger"
                                        outlined
                                        size="small"
                                        class="!text-xs shrink-0"
                                        :loading="deletingInvalidIds.has(cam.id)"
                                        @click="doDeleteInvalidPhotos(cam.id)"
                                    >
                                        <Trash2 class="mr-1 size-3" />
                                        {{ t('calib.step5DeleteInvalid') }}
                                    </Button>
                                </div>

                                <TabPanels>
                                    <!-- 内参照片 -->
                                    <TabPanel value="intrinsic">
                                        <div class="pt-2">
                                            <div
                                                v-if="
                                                    !intrinsicPhotosMap[cam.id] ||
                                                    intrinsicPhotosMap[cam.id].length === 0
                                                "
                                                class="py-8 text-center text-xs text-muted-foreground"
                                            >
                                                {{ t('calib.step5PhotoCount', { count: 0 }) }}
                                            </div>
                                            <div v-else class="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-5">
                                                <div
                                                    v-for="photo in intrinsicPhotosMap[cam.id]"
                                                    :key="photo.id"
                                                    class="group relative overflow-hidden rounded-lg border"
                                                    :class="photo.isValid ? 'border-green-500/40' : 'border-red-500/40'"
                                                >
                                                    <img
                                                        v-if="photo.thumbnailBase64"
                                                        :src="photo.thumbnailBase64"
                                                        class="aspect-square w-full object-cover"
                                                        :alt="photo.capturedAt"
                                                    />
                                                    <div
                                                        v-else
                                                        class="flex aspect-square items-center justify-center bg-muted/20"
                                                    >
                                                        <Camera class="size-6 text-muted-foreground/30" />
                                                    </div>

                                                    <!-- 有效/无效标签 -->
                                                    <div
                                                        class="absolute left-1 top-1 rounded px-1 py-0.5 text-[9px] font-semibold"
                                                        :class="
                                                            photo.isValid
                                                                ? 'bg-green-600/80 text-white'
                                                                : 'bg-red-600/80 text-white'
                                                        "
                                                    >
                                                        {{
                                                            photo.isValid
                                                                ? t('calib.step5ValidPhoto')
                                                                : t('calib.step5InvalidPhoto').slice(0, 2)
                                                        }}
                                                    </div>

                                                    <!-- 删除按钮（hover 显示） -->
                                                    <button
                                                        class="absolute right-1 top-1 rounded bg-black/60 p-0.5 opacity-0 transition-opacity group-hover:opacity-100"
                                                        @click.stop="doDeletePhoto(photo, cam.id)"
                                                    >
                                                        <Trash2 class="size-3 text-white" />
                                                    </button>
                                                </div>
                                            </div>
                                        </div>
                                    </TabPanel>

                                    <!-- 外参照片 -->
                                    <TabPanel v-if="canUseProjectorExtrinsic(cam.id)" value="extrinsic">
                                        <div class="pt-2">
                                            <div
                                                v-if="
                                                    !extrinsicPhotosMap[cam.id] ||
                                                    extrinsicPhotosMap[cam.id].length === 0
                                                "
                                                class="py-8 text-center text-xs text-muted-foreground"
                                            >
                                                {{ t('calib.step5PhotoCount', { count: 0 }) }}
                                            </div>
                                            <div v-else class="space-y-3">
                                                <div
                                                    v-for="sample in getExtrinsicSamples(cam.id)"
                                                    :key="sample.pairGroupId"
                                                    class="group relative overflow-hidden rounded-lg border p-3"
                                                    :class="
                                                        sample.isValid ? 'border-green-500/40' : 'border-red-500/40'
                                                    "
                                                >
                                                    <div class="mb-3 flex items-center justify-between gap-2">
                                                        <div class="flex items-center gap-2">
                                                            <Tag
                                                                :severity="sample.isValid ? 'success' : 'danger'"
                                                                :value="sample.isValid ? 'OK' : 'NG'"
                                                            />
                                                            <span class="text-xs text-muted-foreground">
                                                                {{ sample.stripeFrameCount }} 帧
                                                                <span v-if="sample.invalidFrameCount > 0">
                                                                    · {{ sample.invalidFrameCount }} 张异常
                                                                </span>
                                                            </span>
                                                        </div>
                                                        <span class="text-[11px] text-muted-foreground">
                                                            {{
                                                                new Date(
                                                                    sample.projectorOffPhoto.capturedAt
                                                                ).toLocaleString()
                                                            }}
                                                        </span>
                                                    </div>

                                                    <div class="grid grid-cols-1 gap-3 lg:grid-cols-2">
                                                        <div>
                                                            <div
                                                                class="mb-1 text-[10px] font-medium text-muted-foreground"
                                                            >
                                                                {{ t('calib.step5ExtrinsicLedOffFrame') }}
                                                            </div>
                                                            <div
                                                                class="relative overflow-hidden rounded border"
                                                                :class="
                                                                    sample.projectorOffPhoto.isValid
                                                                        ? 'border-green-500/30'
                                                                        : 'border-red-500/40'
                                                                "
                                                            >
                                                                <img
                                                                    v-if="sample.projectorOffPhoto.thumbnailBase64"
                                                                    :src="sample.projectorOffPhoto.thumbnailBase64"
                                                                    class="w-full rounded object-contain"
                                                                    :alt="sample.projectorOffPhoto.capturedAt"
                                                                />
                                                                <div
                                                                    v-else
                                                                    class="flex aspect-square items-center justify-center rounded bg-muted/20"
                                                                >
                                                                    <Camera class="size-6 text-muted-foreground/30" />
                                                                </div>
                                                                <div
                                                                    class="absolute left-1 top-1 rounded bg-black/65 px-1 py-0.5 text-[9px] text-white"
                                                                >
                                                                    白屏
                                                                </div>
                                                                <div
                                                                    class="absolute right-1 top-1 rounded px-1 py-0.5 text-[9px] font-semibold text-white"
                                                                    :class="
                                                                        sample.projectorOffPhoto.isValid
                                                                            ? 'bg-green-600/80'
                                                                            : 'bg-red-600/80'
                                                                    "
                                                                >
                                                                    {{ sample.projectorOffPhoto.isValid ? 'OK' : 'NG' }}
                                                                </div>
                                                            </div>
                                                        </div>

                                                        <div>
                                                            <div class="mb-1 flex items-center justify-between gap-2">
                                                                <span
                                                                    class="text-[10px] font-medium text-muted-foreground"
                                                                >
                                                                    {{ t('calib.step5ExtrinsicLedOnFrame') }}
                                                                </span>
                                                                <span class="text-[10px] text-muted-foreground">
                                                                    {{ sample.projectorOnPhotos.length }} 张
                                                                </span>
                                                            </div>
                                                            <div
                                                                class="grid grid-cols-3 gap-2 sm:grid-cols-4 xl:grid-cols-6"
                                                            >
                                                                <div
                                                                    v-for="(
                                                                        stripePhoto, stripeIndex
                                                                    ) in sample.projectorOnPhotos"
                                                                    :key="stripePhoto.id"
                                                                    class="space-y-1"
                                                                >
                                                                    <div
                                                                        class="relative overflow-hidden rounded border"
                                                                        :class="
                                                                            stripePhoto.isValid
                                                                                ? 'border-green-500/30'
                                                                                : 'border-red-500/40'
                                                                        "
                                                                    >
                                                                        <img
                                                                            v-if="stripePhoto.thumbnailBase64"
                                                                            :src="stripePhoto.thumbnailBase64"
                                                                            class="aspect-square w-full rounded object-cover"
                                                                            :alt="stripePhoto.capturedAt"
                                                                        />
                                                                        <div
                                                                            v-else
                                                                            class="flex aspect-square items-center justify-center rounded bg-muted/20"
                                                                        >
                                                                            <Zap
                                                                                class="size-6 text-muted-foreground/30"
                                                                            />
                                                                        </div>
                                                                        <div
                                                                            class="absolute left-1 top-1 rounded bg-black/65 px-1 py-0.5 text-[9px] text-white"
                                                                        >
                                                                            #{{ stripeIndex + 1 }}
                                                                        </div>
                                                                        <div
                                                                            class="absolute right-1 top-1 rounded px-1 py-0.5 text-[9px] font-semibold text-white"
                                                                            :class="
                                                                                stripePhoto.isValid
                                                                                    ? 'bg-green-600/80'
                                                                                    : 'bg-red-600/80'
                                                                            "
                                                                        >
                                                                            {{ stripePhoto.isValid ? 'OK' : 'NG' }}
                                                                        </div>
                                                                    </div>
                                                                    <div
                                                                        class="truncate text-[10px] text-muted-foreground"
                                                                    >
                                                                        {{ stripePhoto.cornerCountDetected }} corners
                                                                    </div>
                                                                </div>
                                                            </div>
                                                        </div>
                                                    </div>

                                                    <button
                                                        class="absolute right-1 top-1 rounded bg-black/60 p-0.5 opacity-0 transition-opacity group-hover:opacity-100"
                                                        @click.stop="doDeletePhoto(sample.projectorOffPhoto, cam.id)"
                                                    >
                                                        <Trash2 class="size-3 text-white" />
                                                    </button>
                                                </div>
                                            </div>
                                        </div>
                                    </TabPanel>

                                    <TabPanel v-if="isStereoProject" value="stereo-pair">
                                        <div class="pt-2">
                                            <div
                                                v-if="
                                                    !stereoPairPhotosMap[cam.id] ||
                                                    stereoPairPhotosMap[cam.id].length === 0
                                                "
                                                class="py-8 text-center text-xs text-muted-foreground"
                                            >
                                                {{ t('calib.step5PhotoCount', { count: 0 }) }}
                                            </div>
                                            <div v-else class="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-5">
                                                <div
                                                    v-for="photo in stereoPairPhotosMap[cam.id]"
                                                    :key="photo.id"
                                                    class="group relative overflow-hidden rounded-lg border"
                                                    :class="photo.isValid ? 'border-green-500/40' : 'border-red-500/40'"
                                                >
                                                    <img
                                                        v-if="photo.thumbnailBase64"
                                                        :src="photo.thumbnailBase64"
                                                        class="aspect-square w-full object-cover"
                                                        :alt="photo.capturedAt"
                                                    />
                                                    <div
                                                        v-else
                                                        class="flex aspect-square items-center justify-center bg-muted/20"
                                                    >
                                                        <Camera class="size-6 text-muted-foreground/30" />
                                                    </div>

                                                    <div
                                                        class="absolute left-1 top-1 rounded px-1 py-0.5 text-[9px] font-semibold"
                                                        :class="
                                                            photo.isValid
                                                                ? 'bg-green-600/80 text-white'
                                                                : 'bg-red-600/80 text-white'
                                                        "
                                                    >
                                                        {{
                                                            photo.isValid
                                                                ? t('calib.step5ValidPhoto')
                                                                : t('calib.step5InvalidPhoto').slice(0, 2)
                                                        }}
                                                    </div>

                                                    <button
                                                        class="absolute right-1 top-1 rounded bg-black/60 p-0.5 opacity-0 transition-opacity group-hover:opacity-100"
                                                        @click.stop="doDeletePhoto(photo, cam.id)"
                                                    >
                                                        <Trash2 class="size-3 text-white" />
                                                    </button>
                                                </div>
                                            </div>
                                        </div>
                                    </TabPanel>
                                </TabPanels>
                            </Tabs>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
