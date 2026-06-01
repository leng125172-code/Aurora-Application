<script setup lang="ts">
/**
 * 标定向导页面（6步 Stepper）
 *
 * Step 1 — 工程信息：查看工程与关联设备基础信息，可修改名称/描述
 * Step 2 — 设备绑定：只读展示相机/电机/投射器绑定关系
 * Step 3 — 硬件参数：逐相机填写 CMOS 参数并保存
 * Step 4 — 标定板 & 采集：配置标定板参数、触发采集、管理帧
 * Step 5 — 内外参计算：触发 Hangfire 计算任务，实时追踪 SignalR 进度
 * Step 6 — 结果 & 导出：查看 RMS 误差，多格式下载标定结果
 */
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import Stepper from 'primevue/stepper'
import StepList from 'primevue/steplist'
import Step from 'primevue/step'
import StepPanels from 'primevue/steppanels'
import StepPanel from 'primevue/steppanel'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import ProgressBar from 'primevue/progressbar'
import ConfirmDialog from 'primevue/confirmdialog'
import Textarea from 'primevue/textarea'
import {
    ArrowLeft,
    ArrowRight,
    Save,
    Camera,
    Zap,
    CheckCircle,
    XCircle,
    Trash2,
    Download,
    RefreshCw,
    Loader2,
} from '@lucide/vue'
import {
    CalibrationBoardType,
    CalibrationProjectStatus,
    CalibrationResultExportFormat,
    CameraRole,
    CmosSensorSize,
    ImageCaptureFormat,
    type CalibrationProjectDetailDto,
    type CalibrationDeviceDetailDto,
    type CalibrationResultDetailDto,
    type CalibrationCaptureFrameDto,
    type SetBoardConfigInput,
    type SetCaptureConfigInput,
    type CreateUpdateCameraParameterInput,
    getCalibrationProjectAsync,
    getCalibrationDeviceAsync,
    updateCalibrationProjectAsync,
    setBoardConfigAsync,
    setCaptureConfigAsync,
    captureFrameAsync,
    acceptFrameAsync,
    rejectFrameAsync,
    deleteFrameAsync,
    computeCalibrationAsync,
    getCalibrationResultListAsync,
    getCalibrationResultAsync,
    setCalibrationResultActiveAsync,
    exportCalibrationResultAsync,
    upsertCameraParameterAsync,
} from '@/api/calibration'
import { useCalibrationStore } from '@/stores/calibration'
import { useAppToast } from '@/composables/useAppToast'

const route = useRoute()
const router = useRouter()
const toast = useAppToast()
const confirm = useConfirm()
const calibStore = useCalibrationStore()

const projectId = computed(() => route.params.id as string)

// ===================== 全局加载状态 =====================

const pageLoading = ref(false)
const project = ref<CalibrationProjectDetailDto | null>(null)
const device = ref<CalibrationDeviceDetailDto | null>(null)
const activeStep = ref<number>(1)

async function loadProject(): Promise<void> {
    pageLoading.value = true
    try {
        const p = await getCalibrationProjectAsync(projectId.value)
        project.value = p
        // 根据工程状态决定跳转到哪一步
        activeStep.value = mapStatusToStep(p.status)
        if (p.calibrationDeviceId) {
            device.value = await getCalibrationDeviceAsync(p.calibrationDeviceId)
        }
    } catch {
        toast.error('加载工程详情失败')
    } finally {
        pageLoading.value = false
    }
}

/** 将工程状态映射到建议步骤 */
function mapStatusToStep(status: CalibrationProjectStatus): number {
    switch (status) {
        case CalibrationProjectStatus.Draft:
            return 1
        case CalibrationProjectStatus.Configuring:
            return 3
        case CalibrationProjectStatus.Capturing:
            return 4
        case CalibrationProjectStatus.Computing:
            return 5
        case CalibrationProjectStatus.Validating:
        case CalibrationProjectStatus.Completed:
        case CalibrationProjectStatus.Failed:
            return 6
        default:
            return 1
    }
}

onMounted(async () => {
    await loadProject()
    await calibStore.startHub()
})

onBeforeUnmount(async () => {
    await calibStore.stopHub()
})

// ===================================================================
// Step 1 — 工程信息
// ===================================================================

const step1Saving = ref(false)
const step1Form = ref({ name: '', description: '' })

watch(
    project,
    (p) => {
        if (p) {
            step1Form.value = { name: p.name, description: p.description ?? '' }
        }
    },
    { immediate: true }
)

async function saveProjectInfo(): Promise<void> {
    if (!step1Form.value.name.trim()) {
        toast.warn('工程名称不能为空')
        return
    }
    step1Saving.value = true
    try {
        const updated = await updateCalibrationProjectAsync(projectId.value, step1Form.value)
        project.value = updated
        toast.success('工程信息已保存')
    } catch {
        toast.error('保存失败')
    } finally {
        step1Saving.value = false
    }
}

/** 设备类型可读标签 */
function deviceTypeLabel(type: number): string {
    const map: Record<number, string> = {
        0: '双相机（无结构光）',
        1: '三相机（无结构光）',
        10: '单相机 + 单投射器',
        11: '双相机 + 单投射器',
        12: '三相机 + 单投射器',
    }
    return map[type] ?? `类型 ${type}`
}

// ===================================================================
// Step 2 — 设备绑定（只读）
// ===================================================================

const cameraRoleLabel: Record<CameraRole, string> = {
    [CameraRole.Master]: '主相机',
    [CameraRole.LeftSlave]: '左从相机',
    [CameraRole.RightSlave]: '右从相机',
    [CameraRole.TopMaster]: '顶部主相机',
    [CameraRole.LeftBottomSlave]: '左下从相机',
    [CameraRole.RightBottomSlave]: '右下从相机',
}

// ===================================================================
// Step 3 — 硬件参数
// ===================================================================

const step3Saving = ref(false)

/** 每个相机绑定对应的参数表单（key = cameraDeviceId） */
const cameraParamForms = ref<
    Map<
        string,
        {
            cameraDeviceId: string
            role: CameraRole
            cmosSize: CmosSensorSize
            cmosWidthMm: number
            cmosHeightMm: number
            resolutionWidthPx: number
            resolutionHeightPx: number
            nominalFocalLengthMm: number
            maxAperture: number
            minAperture: number
            currentAperture: number
            minExposureUs: number
            maxExposureUs: number
            minGainDb: number
            maxGainDb: number
        }
    >
>(new Map())

/** 初始化 Step3 表单（绑定 device 后或进入此步时调用） */
function initStep3Forms(): void {
    if (!device.value) return
    const map = new Map<string, ReturnType<typeof buildDefaultCameraParamForm>>()
    for (const binding of device.value.cameraBindings) {
        // 优先从已有参数中加载
        const existing = device.value.cameraParameters.find(
            (p) => p.cameraDeviceId === binding.cameraDeviceId
        )
        map.set(binding.cameraDeviceId, {
            cameraDeviceId: binding.cameraDeviceId,
            role: binding.role,
            cmosSize: existing?.cmosSize ?? CmosSensorSize.OneThirdInch,
            cmosWidthMm: existing?.cmosWidthMm ?? 5.76,
            cmosHeightMm: existing?.cmosHeightMm ?? 4.29,
            resolutionWidthPx: existing?.resolutionWidthPx ?? 1920,
            resolutionHeightPx: existing?.resolutionHeightPx ?? 1080,
            nominalFocalLengthMm: existing?.nominalFocalLengthMm ?? 8.0,
            maxAperture: existing?.maxAperture ?? 1.8,
            minAperture: existing?.minAperture ?? 16.0,
            currentAperture: existing?.currentAperture ?? 4.0,
            minExposureUs: existing?.minExposureUs ?? 100,
            maxExposureUs: existing?.maxExposureUs ?? 500000,
            minGainDb: existing?.minGainDb ?? 0,
            maxGainDb: existing?.maxGainDb ?? 24,
        })
    }
    cameraParamForms.value = map
}

function buildDefaultCameraParamForm(cameraDeviceId: string, role: CameraRole) {
    return {
        cameraDeviceId,
        role,
        cmosSize: CmosSensorSize.OneThirdInch,
        cmosWidthMm: 5.76,
        cmosHeightMm: 4.29,
        resolutionWidthPx: 1920,
        resolutionHeightPx: 1080,
        nominalFocalLengthMm: 8.0,
        maxAperture: 1.8,
        minAperture: 16.0,
        currentAperture: 4.0,
        minExposureUs: 100,
        maxExposureUs: 500000,
        minGainDb: 0,
        maxGainDb: 24,
    }
}

watch(device, () => initStep3Forms(), { immediate: true })

const cmosSizeOptions = [
    { label: '1/4"', value: CmosSensorSize.QuarterInch },
    { label: '1/3"', value: CmosSensorSize.OneThirdInch },
    { label: '1/2.3"', value: CmosSensorSize.OneOverTwoPointThreeInch },
    { label: '1/2"', value: CmosSensorSize.OneSecondInch },
    { label: '2/3"', value: CmosSensorSize.TwoThirdInch },
    { label: '1"', value: CmosSensorSize.OneInch },
    { label: '4/3"', value: CmosSensorSize.FourThirdInch },
    { label: 'APS-C', value: CmosSensorSize.ApsC },
    { label: '全画幅 35mm', value: CmosSensorSize.FullFrame35mm },
    { label: '中画幅', value: CmosSensorSize.MediumFormat },
    { label: '自定义', value: CmosSensorSize.Custom },
]

async function saveCameraParams(): Promise<void> {
    if (!device.value) return
    step3Saving.value = true
    try {
        for (const [, form] of cameraParamForms.value) {
            const input: CreateUpdateCameraParameterInput = { ...form }
            await upsertCameraParameterAsync(device.value.id, input)
        }
        toast.success('相机硬件参数已保存')
        // 刷新设备数据以更新参数列表
        device.value = await getCalibrationDeviceAsync(device.value.id)
        initStep3Forms()
    } catch {
        toast.error('保存硬件参数失败')
    } finally {
        step3Saving.value = false
    }
}

// ===================================================================
// Step 4 — 标定板 & 采集
// ===================================================================

const step4Saving = ref(false)
const capturing = ref(false)

const boardForm = ref<SetBoardConfigInput>({
    boardType: CalibrationBoardType.Chessboard,
    rows: 9,
    cols: 6,
    manufactureAccuracyMm: 0.02,
    squareSizeMm: 25.0,
})

const captureForm = ref<SetCaptureConfigInput>({
    targetCaptureCount: 20,
    unifiedExposureUs: 5000,
    unifiedGainDb: 0,
    imageFormat: ImageCaptureFormat.Gray,
})

const boardTypeOptions = [
    { label: '棋盘格', value: CalibrationBoardType.Chessboard },
    { label: '圆点网格', value: CalibrationBoardType.CircleGrid },
    { label: 'AprilTag', value: CalibrationBoardType.AprilTag },
]

const imageFormatOptions = [
    { label: 'Raw 原始', value: ImageCaptureFormat.Raw },
    { label: 'BGR 彩色', value: ImageCaptureFormat.Bgr },
    { label: '灰度', value: ImageCaptureFormat.Gray },
]

/** 从工程详情中初始化 Step4 表单 */
watch(
    project,
    (p) => {
        if (!p) return
        boardForm.value = {
            boardType: p.boardType ?? CalibrationBoardType.Chessboard,
            rows: p.boardRows ?? 9,
            cols: p.boardCols ?? 6,
            manufactureAccuracyMm: p.boardManufactureAccuracyMm ?? 0.02,
            squareSizeMm: p.squareSizeMm ?? 25.0,
            circleDiameterMm: p.circleDiameterMm,
            circleSpacingMm: p.circleSpacingMm,
            aprilTagFamily: p.aprilTagFamily,
            aprilTagSizeMm: p.aprilTagSizeMm,
            aprilTagSpacingMm: p.aprilTagSpacingMm,
        }
        captureForm.value = {
            targetCaptureCount: p.targetCaptureCount || 20,
            unifiedExposureUs: p.unifiedExposureUs || 5000,
            unifiedGainDb: p.unifiedGainDb || 0,
            imageFormat: p.imageFormat ?? ImageCaptureFormat.Gray,
            structuredLightBrightness: p.structuredLightBrightness,
            patternIntervalMs: p.patternIntervalMs,
            capturesPerPhase: p.capturesPerPhase,
        }
    },
    { immediate: true }
)

async function saveBoardAndCaptureConfig(): Promise<void> {
    step4Saving.value = true
    try {
        let p = await setBoardConfigAsync(projectId.value, boardForm.value)
        p = await setCaptureConfigAsync(projectId.value, captureForm.value)
        project.value = p
        toast.success('标定板与采集参数已保存')
    } catch {
        toast.error('保存配置失败')
    } finally {
        step4Saving.value = false
    }
}

/** 触发单帧采集 */
async function triggerCapture(): Promise<void> {
    capturing.value = true
    try {
        const frame = await captureFrameAsync(projectId.value)
        // 将新帧追加到本地列表（避免全量刷新）
        if (project.value) {
            project.value = {
                ...project.value,
                frames: [...project.value.frames, frame],
                frameCount: project.value.frameCount + 1,
            }
        }
        toast.success(`帧 #${frame.frameIndex} 采集成功`)
    } catch {
        toast.error('采集失败')
    } finally {
        capturing.value = false
    }
}

/** 接受帧 */
async function acceptFrame(frame: CalibrationCaptureFrameDto): Promise<void> {
    try {
        await acceptFrameAsync(projectId.value, frame.id)
        if (project.value) {
            const idx = project.value.frames.findIndex((f) => f.id === frame.id)
            if (idx >= 0) {
                const updatedFrames = [...project.value.frames]
                updatedFrames[idx] = { ...frame, isAccepted: true, rejectionReason: null }
                project.value = {
                    ...project.value,
                    frames: updatedFrames,
                    acceptedFrameCount: project.value.acceptedFrameCount + 1,
                }
            }
        }
        toast.success(`帧 #${frame.frameIndex} 已接受`)
    } catch {
        toast.error('操作失败')
    }
}

/** 拒绝帧 */
async function rejectFrame(frame: CalibrationCaptureFrameDto): Promise<void> {
    try {
        await rejectFrameAsync(projectId.value, frame.id, '手动拒绝')
        if (project.value) {
            const idx = project.value.frames.findIndex((f) => f.id === frame.id)
            if (idx >= 0) {
                const updatedFrames = [...project.value.frames]
                updatedFrames[idx] = { ...frame, isAccepted: false, rejectionReason: '手动拒绝' }
                project.value = {
                    ...project.value,
                    frames: updatedFrames,
                    acceptedFrameCount: Math.max(
                        0,
                        project.value.acceptedFrameCount - (frame.isAccepted ? 1 : 0)
                    ),
                }
            }
        }
        toast.warn(`帧 #${frame.frameIndex} 已拒绝`)
    } catch {
        toast.error('操作失败')
    }
}

/** 删除帧 */
function confirmDeleteFrame(frame: CalibrationCaptureFrameDto): void {
    confirm.require({
        message: `确定要删除帧 #${frame.frameIndex}？`,
        header: '删除确认',
        accept: async () => {
            try {
                await deleteFrameAsync(projectId.value, frame.id)
                if (project.value) {
                    project.value = {
                        ...project.value,
                        frames: project.value.frames.filter((f) => f.id !== frame.id),
                        frameCount: project.value.frameCount - 1,
                        acceptedFrameCount: project.value.acceptedFrameCount - (frame.isAccepted ? 1 : 0),
                    }
                }
                toast.success('帧已删除')
            } catch {
                toast.error('删除失败')
            }
        },
    })
}

// ===================================================================
// Step 5 — 内外参计算
// ===================================================================

const computing = ref(false)
const computeProgress = computed(() => calibStore.getProgress(projectId.value))
const computeCompletion = computed(() => calibStore.getCompletion(projectId.value))

/** 触发标定计算 */
async function startCompute(): Promise<void> {
    calibStore.clearProjectProgress(projectId.value)
    computing.value = true
    try {
        await computeCalibrationAsync(projectId.value)
        toast.success('标定计算已提交，正在处理中…')
    } catch {
        toast.error('提交计算任务失败')
        computing.value = false
        return
    }
    // 等待 SignalR 通知到达后才重置 computing 状态
}

/** 监听完成通知 */
watch(computeCompletion, async (comp) => {
    if (!comp) return
    computing.value = false
    if (comp.success) {
        toast.success('标定计算完成！请前往第 6 步查看结果。')
        // 刷新工程信息并跳转到 Step6
        project.value = await getCalibrationProjectAsync(projectId.value)
        await loadStep6Result()
        activeStep.value = 6
    } else {
        toast.error(`标定计算失败：${comp.errorMessage ?? '未知错误'}`)
    }
})

// ===================================================================
// Step 6 — 结果 & 导出
// ===================================================================

const step6Loading = ref(false)
const result = ref<CalibrationResultDetailDto | null>(null)
const settingActive = ref(false)

const exportFormatOptions = [
    { label: 'JSON', value: CalibrationResultExportFormat.Json, ext: 'json' },
    { label: 'XML', value: CalibrationResultExportFormat.Xml, ext: 'xml' },
    { label: 'YAML', value: CalibrationResultExportFormat.Yaml, ext: 'yaml' },
    { label: 'TXT', value: CalibrationResultExportFormat.Txt, ext: 'txt' },
]

async function loadStep6Result(): Promise<void> {
    step6Loading.value = true
    try {
        const list = await getCalibrationResultListAsync({
            calibrationProjectId: projectId.value,
            maxResultCount: 1,
        })
        if (list.items.length > 0) {
            result.value = await getCalibrationResultAsync(list.items[0].id)
        }
    } catch {
        toast.error('加载标定结果失败')
    } finally {
        step6Loading.value = false
    }
}

/** 在进入 Step6 时自动加载结果 */
watch(activeStep, async (step) => {
    if (step === 6 && !result.value) {
        await loadStep6Result()
    }
})

/** 设为当前生效版本 */
async function setActive(): Promise<void> {
    if (!result.value) return
    settingActive.value = true
    try {
        const updated = await setCalibrationResultActiveAsync(result.value.id)
        result.value = updated
        toast.success('已设为生效版本')
    } catch {
        toast.error('操作失败')
    } finally {
        settingActive.value = false
    }
}

/** 下载导出文件 */
async function exportResult(format: CalibrationResultExportFormat, ext: string): Promise<void> {
    if (!result.value) return
    try {
        const blob = await exportCalibrationResultAsync(result.value.id, format)
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = `calibration-result-v${result.value.version}.${ext}`
        a.click()
        URL.revokeObjectURL(url)
    } catch {
        toast.error('导出失败')
    }
}
</script>

<template>
    <div class="p-4">
        <ConfirmDialog />

        <!-- 顶部导航 -->
        <div class="flex items-center gap-3 mb-4">
            <Button size="small" severity="secondary" @click="router.push({ name: 'CalibrationProjects' })">
                <ArrowLeft class="size-4 mr-1" />
                返回工程列表
            </Button>
            <span class="text-lg font-semibold">
                {{ project?.name ?? '标定向导' }}
            </span>
            <Tag
                v-if="project"
                :value="['草稿','配置中','采集中','计算中','验证中','已完成','失败','已归档'][project.status]"
                :severity="[
                    'secondary','info','info','warn','warn','success','danger','secondary'
                ][project.status]"
                class="text-xs"
            />
        </div>

        <!-- Stepper -->
        <Stepper v-model:value="activeStep" class="w-full">
            <StepList>
                <Step :value="1">工程信息</Step>
                <Step :value="2">设备绑定</Step>
                <Step :value="3">硬件参数</Step>
                <Step :value="4">标定板 &amp; 采集</Step>
                <Step :value="5">内外参计算</Step>
                <Step :value="6">结果 &amp; 导出</Step>
            </StepList>

            <StepPanels>
                <!-- ─────────────────── Step 1 ─────────────────── -->
                <StepPanel :value="1">
                    <div class="py-4 space-y-4 max-w-2xl">
                        <div class="grid grid-cols-2 gap-4 text-sm">
                            <div>
                                <span class="text-muted-foreground">关联设备</span>
                                <p class="font-medium mt-1">{{ device?.name ?? '加载中…' }}</p>
                            </div>
                            <div>
                                <span class="text-muted-foreground">设备类型</span>
                                <p class="font-medium mt-1">
                                    {{ device ? deviceTypeLabel(device.deviceType) : '—' }}
                                </p>
                            </div>
                            <div>
                                <span class="text-muted-foreground">相机绑定数</span>
                                <p class="font-medium mt-1">{{ device?.cameraBindings.length ?? 0 }}</p>
                            </div>
                            <div>
                                <span class="text-muted-foreground">是否含结构光</span>
                                <p class="font-medium mt-1">
                                    {{ device?.hasStructuredLight ? '是' : '否' }}
                                </p>
                            </div>
                        </div>

                        <hr class="border-border" />

                        <div class="space-y-3">
                            <div class="flex flex-col gap-1">
                                <label class="text-sm font-medium">工程名称 *</label>
                                <InputText v-model="step1Form.name" class="w-full" />
                            </div>
                            <div class="flex flex-col gap-1">
                                <label class="text-sm font-medium">备注</label>
                                <Textarea v-model="step1Form.description" rows="3" class="w-full" />
                            </div>
                        </div>

                        <div class="flex justify-between pt-2">
                            <Button severity="secondary" :loading="step1Saving" @click="saveProjectInfo">
                                <Save class="size-4 mr-1" />
                                保存
                            </Button>
                            <Button @click="activeStep = 2">
                                下一步
                                <ArrowRight class="size-4 ml-1" />
                            </Button>
                        </div>
                    </div>
                </StepPanel>

                <!-- ─────────────────── Step 2 ─────────────────── -->
                <StepPanel :value="2">
                    <div class="py-4 space-y-4">
                        <template v-if="device">
                            <!-- 相机绑定 -->
                            <div>
                                <p class="text-sm font-semibold mb-2">相机绑定（{{ device.cameraBindings.length }} 台）</p>
                                <DataTable :value="device.cameraBindings" size="small" striped-rows>
                                    <Column field="cameraDeviceId" header="相机设备 ID" style="font-size:12px" />
                                    <Column header="逻辑角色">
                                        <template #body="{ data }">
                                            {{ cameraRoleLabel[data.role as CameraRole] ?? `角色 ${data.role}` }}
                                        </template>
                                    </Column>
                                </DataTable>
                            </div>

                            <!-- 电机绑定 -->
                            <div>
                                <p class="text-sm font-semibold mb-2">电机绑定（{{ device.motorBindings.length }} 台）</p>
                                <DataTable :value="device.motorBindings" size="small" striped-rows>
                                    <Column field="motorAxisId" header="电机轴 ID" style="font-size:12px" />
                                    <Column field="role" header="角色" />
                                </DataTable>
                            </div>

                            <!-- 投射器绑定 -->
                            <div v-if="device.projectorBindings.length > 0">
                                <p class="text-sm font-semibold mb-2">投射器绑定（{{ device.projectorBindings.length }} 台）</p>
                                <DataTable :value="device.projectorBindings" size="small" striped-rows>
                                    <Column field="projectorDeviceId" header="投射器设备 ID" style="font-size:12px" />
                                    <Column field="role" header="角色" />
                                </DataTable>
                            </div>
                        </template>
                        <p v-else class="text-muted-foreground text-sm">设备信息加载中…</p>

                        <div class="flex justify-between pt-2">
                            <Button severity="secondary" @click="activeStep = 1">
                                <ArrowLeft class="size-4 mr-1" />
                                上一步
                            </Button>
                            <Button @click="activeStep = 3">
                                下一步
                                <ArrowRight class="size-4 ml-1" />
                            </Button>
                        </div>
                    </div>
                </StepPanel>

                <!-- ─────────────────── Step 3 ─────────────────── -->
                <StepPanel :value="3">
                    <div class="py-4 space-y-6">
                        <p class="text-sm text-muted-foreground">
                            为每台相机填写 CMOS 传感器及镜头参数，用于后续标定计算。
                        </p>

                        <template v-for="[camId, form] in cameraParamForms" :key="camId">
                            <div class="border border-border rounded-lg p-4 space-y-3">
                                <p class="text-sm font-semibold">
                                    {{ cameraRoleLabel[form.role] ?? `角色 ${form.role}` }}
                                    <span class="text-xs text-muted-foreground ml-2">{{ camId }}</span>
                                </p>

                                <div class="grid grid-cols-2 gap-3 text-sm">
                                    <div class="flex flex-col gap-1">
                                        <label>CMOS 尺寸</label>
                                        <Select
                                            v-model="form.cmosSize"
                                            :options="cmosSizeOptions"
                                            option-label="label"
                                            option-value="value"
                                            class="w-full"
                                        />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>焦距 (mm)</label>
                                        <InputNumber v-model="form.nominalFocalLengthMm" :min="1" :max="2000" :step="0.1" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>分辨率宽 (px)</label>
                                        <InputNumber v-model="form.resolutionWidthPx" :min="1" :max="20000" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>分辨率高 (px)</label>
                                        <InputNumber v-model="form.resolutionHeightPx" :min="1" :max="20000" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>CMOS 宽 (mm)</label>
                                        <InputNumber v-model="form.cmosWidthMm" :min="0.1" :max="100" :step="0.01" :minFractionDigits="2" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>CMOS 高 (mm)</label>
                                        <InputNumber v-model="form.cmosHeightMm" :min="0.1" :max="100" :step="0.01" :minFractionDigits="2" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>当前光圈 (f/)</label>
                                        <InputNumber v-model="form.currentAperture" :min="0.8" :max="32" :step="0.1" :minFractionDigits="1" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>最大曝光 (μs)</label>
                                        <InputNumber v-model="form.maxExposureUs" :min="100" :max="1000000" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>最小增益 (dB)</label>
                                        <InputNumber v-model="form.minGainDb" :min="0" :max="100" :step="0.1" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>最大增益 (dB)</label>
                                        <InputNumber v-model="form.maxGainDb" :min="0" :max="100" :step="0.1" class="w-full" />
                                    </div>
                                </div>
                            </div>
                        </template>

                        <div v-if="cameraParamForms.size === 0" class="text-muted-foreground text-sm">
                            当前设备无相机绑定，请先完成设备绑定配置。
                        </div>

                        <div class="flex justify-between pt-2">
                            <Button severity="secondary" @click="activeStep = 2">
                                <ArrowLeft class="size-4 mr-1" />
                                上一步
                            </Button>
                            <div class="flex gap-2">
                                <Button severity="secondary" :loading="step3Saving" @click="saveCameraParams">
                                    <Save class="size-4 mr-1" />
                                    保存硬件参数
                                </Button>
                                <Button @click="activeStep = 4">
                                    下一步
                                    <ArrowRight class="size-4 ml-1" />
                                </Button>
                            </div>
                        </div>
                    </div>
                </StepPanel>

                <!-- ─────────────────── Step 4 ─────────────────── -->
                <StepPanel :value="4">
                    <div class="py-4 space-y-4">
                        <div class="grid grid-cols-2 gap-6">
                            <!-- 左：配置表单 -->
                            <div class="space-y-4">
                                <p class="text-sm font-semibold">标定板参数</p>
                                <div class="space-y-3 text-sm">
                                    <div class="flex flex-col gap-1">
                                        <label>标定板类型</label>
                                        <Select
                                            v-model="boardForm.boardType"
                                            :options="boardTypeOptions"
                                            option-label="label"
                                            option-value="value"
                                            class="w-full"
                                        />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>行数（角点）</label>
                                        <InputNumber v-model="boardForm.rows" :min="3" :max="30" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>列数（角点）</label>
                                        <InputNumber v-model="boardForm.cols" :min="3" :max="30" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>格/圆间距 (mm)</label>
                                        <InputNumber
                                            v-model="boardForm.squareSizeMm"
                                            :min="0.1"
                                            :max="500"
                                            :step="0.1"
                                            :minFractionDigits="1"
                                            class="w-full"
                                        />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>制造精度 (mm)</label>
                                        <InputNumber
                                            v-model="boardForm.manufactureAccuracyMm"
                                            :min="0.001"
                                            :max="1"
                                            :step="0.001"
                                            :minFractionDigits="3"
                                            class="w-full"
                                        />
                                    </div>
                                </div>

                                <p class="text-sm font-semibold pt-2">采集参数</p>
                                <div class="space-y-3 text-sm">
                                    <div class="flex flex-col gap-1">
                                        <label>目标采集帧数</label>
                                        <InputNumber v-model="captureForm.targetCaptureCount" :min="5" :max="200" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>统一曝光时间 (μs)</label>
                                        <InputNumber v-model="captureForm.unifiedExposureUs" :min="100" :max="1000000" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>统一增益 (dB)</label>
                                        <InputNumber v-model="captureForm.unifiedGainDb" :min="0" :max="48" :step="0.1" :minFractionDigits="1" class="w-full" />
                                    </div>
                                    <div class="flex flex-col gap-1">
                                        <label>图像格式</label>
                                        <Select
                                            v-model="captureForm.imageFormat"
                                            :options="imageFormatOptions"
                                            option-label="label"
                                            option-value="value"
                                            class="w-full"
                                        />
                                    </div>
                                </div>

                                <div class="flex gap-2 pt-2">
                                    <Button :loading="step4Saving" @click="saveBoardAndCaptureConfig">
                                        <Save class="size-4 mr-1" />
                                        保存配置
                                    </Button>
                                    <Button severity="secondary" :loading="capturing" @click="triggerCapture">
                                        <Camera class="size-4 mr-1" />
                                        触发采集
                                    </Button>
                                </div>
                            </div>

                            <!-- 右：帧列表 -->
                            <div class="space-y-2">
                                <div class="flex items-center justify-between">
                                    <p class="text-sm font-semibold">
                                        已采集帧（{{ project?.acceptedFrameCount ?? 0 }} / {{ project?.targetCaptureCount || '—' }} 已接受）
                                    </p>
                                    <Button size="small" severity="secondary" :loading="pageLoading" @click="loadProject">
                                        <RefreshCw class="size-3.5" />
                                    </Button>
                                </div>

                                <DataTable
                                    :value="project?.frames ?? []"
                                    size="small"
                                    striped-rows
                                    :rows="10"
                                    paginator
                                    scroll-height="380px"
                                    scrollable
                                >
                                    <Column field="frameIndex" header="#" style="width:50px" />
                                    <Column header="状态">
                                        <template #body="{ data }">
                                            <Tag
                                                v-if="data.isAccepted"
                                                value="已接受"
                                                severity="success"
                                            />
                                            <Tag
                                                v-else-if="data.rejectionReason"
                                                value="已拒绝"
                                                severity="danger"
                                            />
                                            <Tag v-else value="待审核" severity="secondary" />
                                        </template>
                                    </Column>
                                    <Column header="操作" style="width:140px">
                                        <template #body="{ data }">
                                            <div class="flex gap-1">
                                                <Button
                                                    v-if="!data.isAccepted"
                                                    size="small"
                                                    severity="success"
                                                    @click="acceptFrame(data)"
                                                >
                                                    <CheckCircle class="size-3" />
                                                </Button>
                                                <Button
                                                    v-if="data.isAccepted"
                                                    size="small"
                                                    severity="warn"
                                                    @click="rejectFrame(data)"
                                                >
                                                    <XCircle class="size-3" />
                                                </Button>
                                                <Button
                                                    size="small"
                                                    severity="danger"
                                                    @click="confirmDeleteFrame(data)"
                                                >
                                                    <Trash2 class="size-3" />
                                                </Button>
                                            </div>
                                        </template>
                                    </Column>
                                </DataTable>
                            </div>
                        </div>

                        <div class="flex justify-between pt-2">
                            <Button severity="secondary" @click="activeStep = 3">
                                <ArrowLeft class="size-4 mr-1" />
                                上一步
                            </Button>
                            <Button @click="activeStep = 5">
                                下一步
                                <ArrowRight class="size-4 ml-1" />
                            </Button>
                        </div>
                    </div>
                </StepPanel>

                <!-- ─────────────────── Step 5 ─────────────────── -->
                <StepPanel :value="5">
                    <div class="py-4 space-y-6 max-w-2xl">
                        <!-- 采集摘要 -->
                        <div class="bg-muted rounded-lg p-4 text-sm space-y-1">
                            <p>
                                已接受帧数：
                                <strong>{{ project?.acceptedFrameCount ?? 0 }}</strong>
                                /
                                目标：<strong>{{ project?.targetCaptureCount || '未设置' }}</strong>
                            </p>
                            <p v-if="(project?.acceptedFrameCount ?? 0) < 10" class="text-destructive">
                                建议至少采集 10 帧以确保标定精度
                            </p>
                        </div>

                        <!-- 实时进度 -->
                        <div v-if="computeProgress" class="space-y-2">
                            <div class="flex items-center justify-between text-sm">
                                <span>{{ computeProgress.stage }}</span>
                                <span>{{ computeProgress.percent }}%</span>
                            </div>
                            <ProgressBar :value="computeProgress.percent" />
                            <p v-if="computeProgress.message" class="text-xs text-muted-foreground">
                                {{ computeProgress.message }}
                            </p>
                        </div>

                        <!-- 完成通知 -->
                        <div
                            v-if="computeCompletion"
                            class="rounded-lg p-3 text-sm"
                            :class="computeCompletion.success
                                ? 'bg-green-950/30 border border-green-800'
                                : 'bg-red-950/30 border border-red-800'"
                        >
                            <template v-if="computeCompletion.success">
                                <div class="flex items-center gap-2">
                                    <CheckCircle class="size-4 text-green-500" />
                                    标定计算成功完成，结果 ID：{{ computeCompletion.resultId }}
                                </div>
                            </template>
                            <template v-else>
                                <div class="flex items-center gap-2">
                                    <XCircle class="size-4 text-red-500" />
                                    计算失败：{{ computeCompletion.errorMessage }}
                                </div>
                            </template>
                        </div>

                        <!-- 操作按钮 -->
                        <div class="flex gap-3">
                            <Button
                                :loading="computing"
                                :disabled="computing"
                                @click="startCompute"
                            >
                                <Zap class="size-4 mr-1" />
                                {{ computing ? '计算中…' : '开始标定计算' }}
                            </Button>
                            <Button
                                v-if="computeCompletion?.success"
                                severity="secondary"
                                @click="activeStep = 6"
                            >
                                查看结果
                                <ArrowRight class="size-4 ml-1" />
                            </Button>
                        </div>

                        <div class="flex justify-between pt-2">
                            <Button severity="secondary" @click="activeStep = 4">
                                <ArrowLeft class="size-4 mr-1" />
                                上一步
                            </Button>
                        </div>
                    </div>
                </StepPanel>

                <!-- ─────────────────── Step 6 ─────────────────── -->
                <StepPanel :value="6">
                    <div class="py-4 space-y-4">
                        <div v-if="step6Loading" class="flex items-center gap-2 text-muted-foreground text-sm">
                            <Loader2 class="size-4 animate-spin" />
                            加载标定结果…
                        </div>

                        <template v-else-if="result">
                            <!-- 误差摘要卡片 -->
                            <div class="grid grid-cols-4 gap-4">
                                <div class="bg-muted rounded-lg p-4 text-center">
                                    <p class="text-xs text-muted-foreground">RMS 误差</p>
                                    <p class="text-2xl font-bold text-primary mt-1">
                                        {{ result.rmsError.toFixed(4) }}
                                    </p>
                                    <p class="text-xs text-muted-foreground">像素</p>
                                </div>
                                <div class="bg-muted rounded-lg p-4 text-center">
                                    <p class="text-xs text-muted-foreground">总体重投影误差</p>
                                    <p class="text-2xl font-bold mt-1">
                                        {{ result.overallReprojectionError.toFixed(4) }}
                                    </p>
                                    <p class="text-xs text-muted-foreground">像素</p>
                                </div>
                                <div class="bg-muted rounded-lg p-4 text-center">
                                    <p class="text-xs text-muted-foreground">最大误差</p>
                                    <p class="text-2xl font-bold mt-1">{{ result.maxError.toFixed(4) }}</p>
                                    <p class="text-xs text-muted-foreground">像素</p>
                                </div>
                                <div class="bg-muted rounded-lg p-4 text-center">
                                    <p class="text-xs text-muted-foreground">版本</p>
                                    <p class="text-2xl font-bold mt-1">v{{ result.version }}</p>
                                    <Tag
                                        v-if="result.isActive"
                                        value="当前生效"
                                        severity="success"
                                        class="mt-1"
                                    />
                                </div>
                            </div>

                            <!-- 计算时间 -->
                            <p class="text-sm text-muted-foreground">
                                计算完成时间：{{ new Date(result.computedTime).toLocaleString('zh-CN') }}
                            </p>

                            <!-- 操作区 -->
                            <div class="flex gap-3 flex-wrap">
                                <Button
                                    v-if="!result.isActive"
                                    severity="secondary"
                                    :loading="settingActive"
                                    @click="setActive"
                                >
                                    <CheckCircle class="size-4 mr-1" />
                                    设为生效版本
                                </Button>

                                <Button
                                    v-for="fmt in exportFormatOptions"
                                    :key="fmt.value"
                                    severity="secondary"
                                    @click="exportResult(fmt.value, fmt.ext)"
                                >
                                    <Download class="size-4 mr-1" />
                                    导出 {{ fmt.label }}
                                </Button>

                                <Button severity="secondary" :loading="step6Loading" @click="loadStep6Result">
                                    <RefreshCw class="size-4 mr-1" />
                                    刷新结果
                                </Button>
                            </div>

                            <!-- 验证记录 -->
                            <div v-if="result.validations.length > 0">
                                <p class="text-sm font-semibold mb-2">验证记录（{{ result.validations.length }}条）</p>
                                <DataTable :value="result.validations" size="small" striped-rows>
                                    <Column header="验证类型">
                                        <template #body="{ data }">
                                            {{
                                                ['畸变校正', '立体匹配', '结构光深度', '总体精度'][data.validationType]
                                                ?? `类型 ${data.validationType}`
                                            }}
                                        </template>
                                    </Column>
                                    <Column header="结果">
                                        <template #body="{ data }">
                                            <Tag
                                                :value="data.isPassed ? '通过' : '未通过'"
                                                :severity="data.isPassed ? 'success' : 'danger'"
                                            />
                                        </template>
                                    </Column>
                                    <Column header="备注" field="remarks" />
                                    <Column header="验证时间">
                                        <template #body="{ data }">
                                            {{ new Date(data.validatedTime).toLocaleString('zh-CN') }}
                                        </template>
                                    </Column>
                                </DataTable>
                            </div>
                        </template>

                        <div v-else class="text-muted-foreground text-sm">
                            暂无标定结果，请先完成第 5 步的标定计算。
                        </div>

                        <div class="flex justify-between pt-2">
                            <Button severity="secondary" @click="activeStep = 5">
                                <ArrowLeft class="size-4 mr-1" />
                                上一步
                            </Button>
                        </div>
                    </div>
                </StepPanel>
            </StepPanels>
        </Stepper>
    </div>
</template>
