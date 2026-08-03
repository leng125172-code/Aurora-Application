<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ChevronDown, ChevronRight } from '@lucide/vue'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { useAppToast } from '@/composables/useAppToast'
import {
    OriginDirection,
    CalibHomingMode,
    getCalibMotorParamListAsync,
    saveCalibMotorParamAsync,
    type CalibMotorParamDto,
    type CalibProjectDto,
} from '@/api/calibration'
import {
    MotorBrand,
    getMotorAxisList,
    refreshMotorStatus,
    setMotorRotationAngleRange,
    type MotorAxisDto,
} from '@/api/motors'
import {
    LeisaiHomingDirection,
    LeisaiHomingMode,
    disableHoming,
    saveHomingConfig,
    saveLimitConfig,
    testHoming,
    type LeisaiHomingConfigInputDto,
} from '@/api/leisai'

// ─── 表单类型 ────────────────────────────────────────────────────────────────

interface Step4MotorForm {
    /** 雷赛回原方向（枚举值直接对应寄存器 Bit0） */
    homingDirection: LeisaiHomingDirection
    homingMode: LeisaiHomingMode
    moveAfterHome: boolean
    withZSignal: boolean
    homeStopPosition: number | null
    homeSpeedRpm: number | null
    homeAccelerationRpm: number | null
    positiveSoftLimit: number | null
    negativeSoftLimit: number | null
    limitEnabled: boolean
}

// ─── Props ───────────────────────────────────────────────────────────────────

const props = defineProps<{
    project: CalibProjectDto | null
}>()

// ─── 公共状态 ─────────────────────────────────────────────────────────────────

const { t } = useI18n()
const toast = useAppToast()

const loading = ref(false)
const motors = ref<MotorAxisDto[]>([])
const motorForms = ref<Record<string, Step4MotorForm>>({})
const expandedMotorId = ref<string | null>(null)
const savingHomeIds = ref<string[]>([])
const savingLimitIds = ref<string[]>([])
const testingHomeIds = ref<string[]>([])
const refreshingIds = ref<string[]>([])

// ─── Select :pt 样式（统一 small 风格） ───────────────────────────────────────

const selectPt = {
    root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
    label: {
        class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
    },
    dropdown: { class: '!w-6 !flex !items-center !justify-center' },
}

// ─── Select 选项 ──────────────────────────────────────────────────────────────

const homingDirectionOptions = computed(() => [
    { label: t('calib.step4OriginDirectionNegative'), value: LeisaiHomingDirection.Negative },
    { label: t('calib.step4OriginDirectionPositive'), value: LeisaiHomingDirection.Positive },
])

const yesNoOptions = computed(() => [
    { label: t('calib.step4OptionNo'), value: false },
    { label: t('calib.step4OptionYes'), value: true },
])

const homingModeOptions = computed(() => [
    { label: t('calib.step4HomeModeLimit'), value: LeisaiHomingMode.Limit },
    { label: t('calib.step4HomeModeOrigin'), value: LeisaiHomingMode.Origin },
])

// ─── 品牌判断 ─────────────────────────────────────────────────────────────────

function isLeisai(axis: MotorAxisDto): boolean {
    return axis.brand === MotorBrand.LeisaiIclRs
}

function isKtech(axis: MotorAxisDto): boolean {
    return axis.brand === MotorBrand.KtechKtech
}

function statusSeverity(axis: MotorAxisDto): 'secondary' | 'success' | 'danger' | 'info' | 'warn' {
    if (axis.status === 3) return 'success'
    if (axis.status === 4) return 'info'
    if (axis.status === 5) return 'danger'
    if (axis.status === 2) return 'secondary'
    return 'warn'
}

// ─── 表单初始化 ───────────────────────────────────────────────────────────────

function createDefaultForm(axis: MotorAxisDto, saved?: CalibMotorParamDto): Step4MotorForm {
    // OriginDirection 与 LeisaiHomingDirection 值统一：Negative=0, Positive=1
    const savedDirection = saved?.originDirection
    let homingDirection: LeisaiHomingDirection
    if (savedDirection === OriginDirection.Positive) {
        homingDirection = LeisaiHomingDirection.Positive
    } else {
        homingDirection = LeisaiHomingDirection.Negative
    }

    return {
        homingDirection,
        homingMode:
            saved?.homingMode != null ? (saved.homingMode as unknown as LeisaiHomingMode) : LeisaiHomingMode.Limit,
        moveAfterHome: saved?.moveAfterHome ?? false,
        withZSignal: saved?.withZSignal ?? false,
        homeStopPosition: saved?.mechanicalOriginPosition != null ? Number(saved.mechanicalOriginPosition) : null,
        homeSpeedRpm: saved?.homeSpeed != null ? Number(saved.homeSpeed) : null,
        homeAccelerationRpm: saved?.homeAcceleration != null ? Number(saved.homeAcceleration) : null,
        positiveSoftLimit:
            saved?.positiveSoftLimit != null
                ? Number(saved.positiveSoftLimit)
                : isKtech(axis)
                  ? (axis.maxRotationAngle ?? null)
                  : null,
        negativeSoftLimit:
            saved?.negativeSoftLimit != null
                ? Number(saved.negativeSoftLimit)
                : isKtech(axis)
                  ? (axis.minRotationAngle ?? null)
                  : null,
        limitEnabled: saved?.limitEnabled ?? isKtech(axis),
    }
}

// ─── 列表加载 ─────────────────────────────────────────────────────────────────

function syncExpandedMotor(nextMotors: MotorAxisDto[]): void {
    if (nextMotors.length === 0) {
        expandedMotorId.value = null
        return
    }
    if (expandedMotorId.value && nextMotors.some((item) => item.id === expandedMotorId.value)) {
        return
    }
    expandedMotorId.value = nextMotors[0]?.id ?? null
}

async function loadStep4(): Promise<void> {
    if (!props.project) {
        motors.value = []
        motorForms.value = {}
        expandedMotorId.value = null
        return
    }

    loading.value = true
    try {
        const [motorResult, savedParams] = await Promise.all([
            getMotorAxisList({ maxResultCount: 200, refreshHardware: true }),
            getCalibMotorParamListAsync(props.project.id),
        ])

        const boundAxisIds = new Set<string>(
            [
                props.project.mainCameraMotorAxisId,
                props.project.secondaryCameraMotorAxisId,
                props.project.distanceMotorAxisId,
            ].filter((id): id is string => !!id)
        )
        const nextMotors = [...motorResult.items]
            .filter((item) => boundAxisIds.has(item.id))
            .sort((left, right) => left.axisIndex - right.axisIndex)
        const savedMap = new Map(savedParams.map((item) => [item.motorAxisId, item]))

        motors.value = nextMotors
        motorForms.value = nextMotors.reduce<Record<string, Step4MotorForm>>((accumulator, axis) => {
            accumulator[axis.id] = createDefaultForm(axis, savedMap.get(axis.id))
            return accumulator
        }, {})
        syncExpandedMotor(nextMotors)
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        loading.value = false
    }
}

watch(
    () => props.project?.id,
    () => {
        void loadStep4()
    },
    { immediate: true }
)

function isBusy(target: typeof savingHomeIds | string[], axisId: string): boolean {
    return Array.isArray(target) ? target.includes(axisId) : target.value.includes(axisId)
}

function setBusy(target: typeof savingHomeIds, axisId: string, value: boolean): void {
    target.value = value ? [...target.value, axisId] : target.value.filter((item: string) => item !== axisId)
}

function toggleMotorExpand(axisId: string): void {
    expandedMotorId.value = expandedMotorId.value === axisId ? null : axisId
}

/** 将表单中的回原方向转换回 calibration OriginDirection（用于保存数据库）。 */
function toOriginDirection(dir: LeisaiHomingDirection): OriginDirection {
    return dir === LeisaiHomingDirection.Positive ? OriginDirection.Positive : OriginDirection.Negative
}

function buildHomingDto(form: Step4MotorForm): LeisaiHomingConfigInputDto {
    return {
        homingDirection: form.homingDirection,
        moveAfterHome: form.moveAfterHome,
        homingMode: form.homingMode,
        withZSignal: form.withZSignal,
        homeStopPosition: form.moveAfterHome ? form.homeStopPosition : null,
        homeSpeedRpm: form.homeSpeedRpm,
        homeAccelerationRpm: form.homeAccelerationRpm,
    }
}

function buildSavePayload(axis: MotorAxisDto, form: Step4MotorForm) {
    return {
        calibProjectId: props.project!.id,
        motorAxisId: axis.id,
        mechanicalOriginPosition: form.homeStopPosition,
        originDirection: toOriginDirection(form.homingDirection),
        homingMode: form.homingMode as unknown as CalibHomingMode,
        moveAfterHome: form.moveAfterHome,
        withZSignal: form.withZSignal,
        positiveSoftLimit: form.positiveSoftLimit,
        negativeSoftLimit: form.negativeSoftLimit,
        homeSpeed: form.homeSpeedRpm,
        homeAcceleration: form.homeAccelerationRpm,
        limitEnabled: form.limitEnabled,
    }
}

// ─── 动作函数 ─────────────────────────────────────────────────────────────────

async function refreshAxis(axis: MotorAxisDto): Promise<void> {
    setBusy(refreshingIds, axis.id, true)
    try {
        await refreshMotorStatus(axis.id)
        await loadStep4()
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        setBusy(refreshingIds, axis.id, false)
    }
}

async function saveHomeConfig(axis: MotorAxisDto): Promise<void> {
    const form = motorForms.value[axis.id]
    if (!props.project || !form) return
    if (form.moveAfterHome && form.homeStopPosition == null) {
        toast.warning(t('calib.step4HomeStopPositionRequired'))
        return
    }

    setBusy(savingHomeIds, axis.id, true)
    try {
        if (isLeisai(axis)) {
            // 后端负责拼装寄存器、写入 EEPROM、启用回原 bit
            await saveHomingConfig(axis.id, buildHomingDto(form))
        }
        // 持久化到数据库
        await saveCalibMotorParamAsync(buildSavePayload(axis, form))
        toast.success(t('calib.step4HomeSaveSuccess'))
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        setBusy(savingHomeIds, axis.id, false)
    }
}

async function disableHomeConfig(axis: MotorAxisDto): Promise<void> {
    const form = motorForms.value[axis.id]
    if (!props.project || !form) return

    setBusy(savingHomeIds, axis.id, true)
    try {
        if (isLeisai(axis)) {
            // 后端清除 0x6000 bit2 并保存 EEPROM
            await disableHoming(axis.id)
        }
        // 所有品牌均同步到数据库（保存当前回原参数）
        await saveCalibMotorParamAsync(buildSavePayload(axis, form))
        toast.success(t('calib.step4HomeDisableSuccess'))
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        setBusy(savingHomeIds, axis.id, false)
    }
}

async function testHome(axis: MotorAxisDto): Promise<void> {
    const form = motorForms.value[axis.id]
    if (!form) return
    if (form.moveAfterHome && form.homeStopPosition == null) {
        toast.warning(t('calib.step4HomeStopPositionRequired'))
        return
    }

    if (isKtech(axis)) {
        toast.warning(t('calib.step4HomingUnsupportedKtech'))
        return
    }

    setBusy(testingHomeIds, axis.id, true)
    try {
        // 后端同步阻塞执行：写参数 → 使能 → 触发 → 轮询完成位 → 关使能
        const result = await testHoming(axis.id, buildHomingDto(form))
        if (result.isCompleted) {
            toast.success(t('calib.step4HomeCompleted'))
        } else {
            toast.warning(t('calib.step4HomeWaitTimeout'))
        }
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        setBusy(testingHomeIds, axis.id, false)
    }
}

async function saveLimitConfigAction(axis: MotorAxisDto): Promise<void> {
    const form = motorForms.value[axis.id]
    if (!props.project || !form) return
    setBusy(savingLimitIds, axis.id, true)
    try {
        if (isKtech(axis)) {
            await setMotorRotationAngleRange(axis.id, {
                minRotationAngle: form.negativeSoftLimit,
                maxRotationAngle: form.positiveSoftLimit,
            })
        } else {
            // 后端负责拼装 Int32 寄存器写入、设置 0x6000 bit1、保存 EEPROM
            await saveLimitConfig(axis.id, {
                limitEnabled: form.limitEnabled,
                positiveSoftLimit: form.positiveSoftLimit,
                negativeSoftLimit: form.negativeSoftLimit,
            })
        }
        // 持久化到数据库
        await saveCalibMotorParamAsync(buildSavePayload(axis, form))
        toast.success(t('calib.step4LimitSaveSuccess'))
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        setBusy(savingLimitIds, axis.id, false)
    }
}
</script>

<template>
    <div class="flex flex-1 min-h-0 flex-col">
        <div class="flex flex-1 min-h-0 flex-col overflow-y-auto">
            <div class="flex items-center justify-between gap-2 border-b border-border/40 px-4 pb-3 pt-3">
                <div>
                    <div class="text-sm font-medium text-foreground">{{ t('calib.step4MotorTitle') }}</div>
                    <div class="text-xs text-muted-foreground">{{ t('calib.step4MotorHint') }}</div>
                </div>
                <Button severity="secondary" outlined size="small" :disabled="loading" @click="loadStep4">
                    {{ t('calib.step4Refresh') }}
                </Button>
            </div>

            <div v-if="loading" class="flex flex-1 items-center justify-center text-sm text-muted-foreground">
                {{ t('common.loading') }}
            </div>

            <div
                v-else-if="motors.length === 0"
                class="flex flex-1 items-center justify-center text-sm text-muted-foreground"
            >
                {{ t('calib.step4NoMotors') }}
            </div>

            <div v-else class="flex flex-col gap-2 p-4 pb-6">
                <div v-for="axis in motors" :key="axis.id" class="overflow-hidden rounded-lg border border-border/50">
                    <div
                        class="flex items-center gap-3 px-4 py-3 transition-colors"
                        :class="{ 'bg-muted/20': expandedMotorId === axis.id }"
                    >
                        <Button unstyled type="button"
                            class="flex min-w-0 flex-1 items-center gap-3 text-left"
                            @click="toggleMotorExpand(axis.id)"
                        >
                            <div class="min-w-0 flex-1 space-y-1">
                                <div class="flex flex-wrap items-center gap-2">
                                    <span class="truncate text-sm font-medium text-foreground">{{ axis.name }}</span>
                                    <Tag :value="axis.brandText" severity="secondary" class="!text-xs" />
                                    <Tag :value="axis.statusText" :severity="statusSeverity(axis)" class="!text-xs" />
                                    <Tag :value="`#${axis.axisIndex + 1}`" severity="contrast" class="!text-xs" />
                                </div>
                                <div class="text-xs text-muted-foreground">
                                    {{ axis.portName }} / Slave {{ axis.slaveId }} / {{ axis.model || '—' }}
                                </div>
                            </div>
                            <ChevronDown
                                v-if="expandedMotorId === axis.id"
                                class="size-4 shrink-0 text-muted-foreground"
                            />
                            <ChevronRight v-else class="size-4 shrink-0 text-muted-foreground" />
                        </Button>
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :loading="isBusy(refreshingIds, axis.id)"
                            @click.stop="refreshAxis(axis)"
                        >
                            {{ t('calib.step4RefreshStatus') }}
                        </Button>
                    </div>

                    <div
                        v-if="expandedMotorId === axis.id"
                        class="border-t border-border/40 bg-background/20 px-5 py-5"
                    >
                        <div class="space-y-5">
                            <div
                                class="rounded-xl border border-border/40 bg-muted/10 px-4 py-3 text-xs leading-5 text-muted-foreground"
                            >
                                <div class="mb-1 font-medium text-foreground/85">
                                    {{ t('calib.step4CapabilityTitle') }}
                                </div>
                                <div>
                                    {{
                                        isKtech(axis)
                                            ? t('calib.step4KtechCapabilityHint')
                                            : t('calib.step4LeisaiCapabilityHint')
                                    }}
                                </div>
                            </div>

                            <div class="rounded-xl border border-border/40 bg-muted/10 px-4 py-4">
                                <h4 class="mb-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                                    {{ t('calib.step4MotorBaseConfig') }}
                                </h4>
                                <div class="grid grid-cols-1 gap-x-6 gap-y-3 text-sm md:grid-cols-2 xl:grid-cols-4">
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4LimitEnabled') }}
                                        </label>
                                        <Select
                                            v-model="motorForms[axis.id].limitEnabled"
                                            size="small"
                                            class="w-full"
                                            :options="yesNoOptions"
                                            option-label="label"
                                            option-value="value"
                                            :pt="selectPt"
                                            :disabled="isKtech(axis)"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4PositiveSoftLimit') }}
                                        </label>
                                        <InputNumber
                                            v-model="motorForms[axis.id].positiveSoftLimit"
                                            size="small"
                                            :use-grouping="false"
                                            :min="-2147483648"
                                            :max="2147483647"
                                            :max-fraction-digits="0"
                                            class="w-full"
                                            :input-class="'!text-xs !h-7 !py-0'"
                                            :disabled="isLeisai(axis) && motorForms[axis.id].limitEnabled"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4NegativeSoftLimit') }}
                                        </label>
                                        <InputNumber
                                            v-model="motorForms[axis.id].negativeSoftLimit"
                                            size="small"
                                            :use-grouping="false"
                                            class="w-full"
                                            :input-class="'!text-xs !h-7 !py-0'"
                                            :disabled="isLeisai(axis) && motorForms[axis.id].limitEnabled"
                                        />
                                    </div>
                                </div>
                                <div class="mt-4 flex flex-wrap justify-end gap-2 border-t border-border/30 pt-3">
                                    <Button
                                        size="small"
                                        :loading="isBusy(savingLimitIds, axis.id)"
                                        class="!text-xs"
                                        @click="saveLimitConfigAction(axis)"
                                    >
                                        {{ t('calib.step4SaveLimit') }}
                                    </Button>
                                </div>
                            </div>

                            <div class="rounded-xl border border-border/40 bg-muted/10 px-4 py-4">
                                <div class="mb-3 flex items-center justify-between gap-2">
                                    <h4 class="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                                        {{ t('calib.step4HomingTitle') }}
                                    </h4>
                                    <Tag
                                        :value="
                                            isKtech(axis)
                                                ? t('calib.step4HomingUnsupportedKtech')
                                                : t('calib.step4HomingSupportedLeisai')
                                        "
                                        :severity="isKtech(axis) ? 'warn' : 'success'"
                                        class="!text-xs"
                                    />
                                </div>
                                <div class="grid grid-cols-1 gap-x-6 gap-y-3 text-sm md:grid-cols-2 xl:grid-cols-4">
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4OriginDirection') }}
                                        </label>
                                        <Select
                                            v-model="motorForms[axis.id].homingDirection"
                                            size="small"
                                            class="w-full"
                                            :options="homingDirectionOptions"
                                            option-label="label"
                                            option-value="value"
                                            :pt="selectPt"
                                            :disabled="isKtech(axis)"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4HomeMode') }}
                                        </label>
                                        <Select
                                            v-model="motorForms[axis.id].homingMode"
                                            size="small"
                                            class="w-full"
                                            :options="homingModeOptions"
                                            option-label="label"
                                            option-value="value"
                                            :pt="selectPt"
                                            :disabled="isKtech(axis)"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4MoveAfterHome') }}
                                        </label>
                                        <Select
                                            v-model="motorForms[axis.id].moveAfterHome"
                                            size="small"
                                            class="w-full"
                                            :options="yesNoOptions"
                                            option-label="label"
                                            option-value="value"
                                            :pt="selectPt"
                                            :disabled="isKtech(axis)"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4WithZSignal') }}
                                        </label>
                                        <Select
                                            v-model="motorForms[axis.id].withZSignal"
                                            size="small"
                                            class="w-full"
                                            :options="yesNoOptions"
                                            option-label="label"
                                            option-value="value"
                                            :pt="selectPt"
                                            :disabled="isKtech(axis)"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4HomeStopPosition') }}
                                        </label>
                                        <InputNumber
                                            v-model="motorForms[axis.id].homeStopPosition"
                                            size="small"
                                            :use-grouping="false"
                                            class="w-full"
                                            :input-class="'!text-xs !h-7 !py-0'"
                                            :disabled="isKtech(axis) || !motorForms[axis.id].moveAfterHome"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4HomeSpeed') }}
                                        </label>
                                        <InputNumber
                                            v-model="motorForms[axis.id].homeSpeedRpm"
                                            size="small"
                                            :use-grouping="false"
                                            class="w-full"
                                            :input-class="'!text-xs !h-7 !py-0'"
                                        />
                                    </div>
                                    <div>
                                        <label class="mb-1 block text-xs text-muted-foreground">
                                            {{ t('calib.step4HomeAcceleration') }}
                                        </label>
                                        <InputNumber
                                            v-model="motorForms[axis.id].homeAccelerationRpm"
                                            size="small"
                                            :use-grouping="false"
                                            class="w-full"
                                            :input-class="'!text-xs !h-7 !py-0'"
                                        />
                                    </div>
                                </div>
                                <div class="mt-4 flex flex-wrap justify-end gap-2 border-t border-border/30 pt-3">
                                    <Button
                                        severity="secondary"
                                        outlined
                                        size="small"
                                        class="!text-xs"
                                        :loading="isBusy(testingHomeIds, axis.id)"
                                        @click="testHome(axis)"
                                    >
                                        {{ t('calib.step4HomeTest') }}
                                    </Button>
                                    <Button
                                        size="small"
                                        class="!text-xs"
                                        :loading="isBusy(savingHomeIds, axis.id)"
                                        @click="saveHomeConfig(axis)"
                                    >
                                        {{ t('calib.step4SaveHome') }}
                                    </Button>
                                    <Button
                                        severity="secondary"
                                        outlined
                                        size="small"
                                        class="!text-xs"
                                        :loading="isBusy(savingHomeIds, axis.id)"
                                        @click="disableHomeConfig(axis)"
                                    >
                                        {{ t('calib.step4DisableHome') }}
                                    </Button>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
