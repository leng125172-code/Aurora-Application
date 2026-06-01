<script setup lang="ts">
/**
 * 瓴控 KTECH 电机操作台
 *  - 5 个功能区：产品信息 / 标定 / 设置 / 运动控制 / 日志
 *  - 通过 SignalR 接收实时状态与升级进度
 *  - REST 调用全部走 @/api/ktech
 */
import { computed, onMounted, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, RefreshCw, Power, PowerOff, CircleStop, Upload, Trash2 } from '@lucide/vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import { AppCard } from '@/components/primevue'
import { useAppToast } from '@/composables/useAppToast'
import * as ktechApi from '@/api/ktech'
import {
    KtechMotionMode,
    KtechUpgradeStage,
    type KtechCalibDto,
    type KtechMotionInputDto,
    type KtechProductInfoDto,
    type KtechSettingDto,
    type KtechWritePidRamInputDto,
} from '@/api/ktech'
import { useKtechMotorStore } from '@/stores/ktech'

const route = useRoute()
const router = useRouter()
const store = useKtechMotorStore()
const { t } = useI18n()
const toast = useAppToast()

const axisId = computed(() => String(route.params.axisId ?? ''))
const activeTab = ref<'info' | 'params' | 'motion' | 'log'>('info')
const acting = ref(false)

// ───── 产品信息 ─────────────────────────────────────────────────────
const productInfo = ref<KtechProductInfoDto | null>(null)
const deviceTypeCode = ref<number | null>(null)

// ───── 标定 ─────────────────────────────────────────────────────────
function emptyCalib(): KtechCalibDto {
    return {
        deviceTypeCode: 0,
        motorPoles: 0,
        encoderType: 0,
        encoderPos: 0,
        motorPhaseSequence: 0,
        motorEncoderAlignBias: 0,
        motorEncoderAlignRatio: 0,
        motorEncoderAlignVoltage: 0,
        motorEncoderAlignFlag: 0,
        encoderOffset: 0,
        encoderOffsetFlag: 0,
        savedFlag: 0,
        mg: null,
    }
}
const calibForm = reactive<KtechCalibDto>(emptyCalib())
// MG/MG_E 型号是否显示扩展字段
const isMgVariant = computed(() => calibForm.deviceTypeCode === 8241 || calibForm.deviceTypeCode === 8242)

// ───── 设置（运行参数） ────────────────────────────────────────────
const settingForm = reactive<KtechSettingDto>(emptySetting())
const pidRamForm = reactive<KtechWritePidRamInputDto>({
    angleKp: 0,
    angleKi: 0,
    angleKd: 0,
    speedKp: 0,
    speedKi: 0,
    speedKd: 0,
    currentKp: 0,
    currentKi: 0,
    currentKd: 0,
})

// ───── 运动控制 ────────────────────────────────────────────────────
const motionForm = reactive<KtechMotionInputDto>({
    mode: KtechMotionMode.Speed,
    voltage: 0,
    torqueOrPower: 0,
    speedDps: 0,
    speedSignedDps: 0,
    angleDeg: 0,
    singleAngleDeg: 0,
    direction: 0,
})

// ───── 升级 ────────────────────────────────────────────────────────
const firmwareFile = ref<File | null>(null)
const firmwareFileName = computed(() => firmwareFile.value?.name ?? null)
const firmwareInputRef = ref<HTMLInputElement | null>(null)
const uploading = ref(false)

// ───── 计算属性：实时状态 ───────────────────────────────────────────
const liveState = computed(() => store.stateByAxis[axisId.value] ?? null)
const upgradeLog = computed(() => store.upgradeLogByAxis[axisId.value] ?? [])

// ───── 实时采样开关 ─────────────────────────────────────────────────
/** true=采样启用（默认），false=已暂停 */
const isSamplingEnabled = ref(true)

/** 切换实时采样开关；ToggleSwitch v-model 已将 isSamplingEnabled 更新为新值后调用。 */
async function toggleSampling(): Promise<void> {
    const id = axisId.value
    if (!id) return
    const newValue = isSamplingEnabled.value // v-model 已更新为期望的新值
    try {
        if (newValue) {
            await ktechApi.enableSampling(id)
        } else {
            await ktechApi.disableSampling(id)
        }
    } catch (err) {
        isSamplingEnabled.value = !newValue // 失败时还原
        toast.error(t('ktechConsole.samplingToggleFailed', { err: String(err) }))
    }
}

function emptySetting(): KtechSettingDto {
    return {
        driverId: 0,
        busType: 0,
        rs485BaudRate: 0,
        canBaudRate: 0,
        broadcastMode: 0,
        spinDirection: 0,
        protectMotorTempEnable: 0,
        protectDriverTempEnable: 0,
        protectUnderVoltageEnable: 0,
        protectOverVoltageEnable: 0,
        protectOverCurrentEnable: 0,
        protectShortCircuitEnable: 0,
        protectStallEnable: 0,
        protectLostInputEnable: 0,
        protectMotorTemp: 0,
        protectDriverTemp: 0,
        protectUnderVoltage: 0,
        protectOverVoltage: 0,
        protectOverCurrent: 0,
        protectOverCurrentTime: 0,
        protectStallTime: 0,
        protectLostInputTime: 0,
        brakeResEnable: 0,
        brakeResOnVoltage: 0,
        inputType: 0,
        pwmInputControlMode: 0,
        pwmInputMinValue: 0,
        pwmInputMaxValue: 0,
        pwmInputCenterValue: 0,
        pwmInputDeadband: 0,
        pwmToTorqueRatio: 0,
        pwmToSpeedRatio: 0,
        pwmToAngleRatio: 0,
        pulsesPerCircle: 0,
        anglePidKp: 0,
        anglePidKi: 0,
        anglePidKd: 0,
        speedPidKp: 0,
        speedPidKi: 0,
        speedPidKd: 0,
        currentPidKp: 0,
        currentPidKi: 0,
        currentPidKd: 0,
        maxTorque: 0,
        maxSpeed: 0,
        maxAngle: 0,
        currentRamp: 0,
        speedRamp: 0,
        uniqueId: 0,
        savedFlag: 0,
    }
}

// ───── 通用：包装 action 显示 toast / loading ──────────────────────
async function runAction<T>(fn: () => Promise<T>, successMsg?: string): Promise<T | undefined> {
    acting.value = true
    try {
        const result = await fn()
        if (successMsg) toast.success(successMsg)
        return result
    } catch (err) {
        const msg = (err as Error)?.message ?? t('ktechConsole.operationFailed')
        toast.error(msg)
        store.pushLog('error', `[操作失败] ${msg}`)
    } finally {
        acting.value = false
    }
    return undefined
}

// ───── Info Tab 操作 ───────────────────────────────────────────────
async function handleLoadProductInfo() {
    const info = await runAction(() => ktechApi.getProductInfo(axisId.value), t('ktechConsole.productInfoLoaded'))
    if (info) {
        productInfo.value = info
        store.cacheProductInfo(axisId.value, info)
        // 产品信息读取后型号可能首次确定，强制刷新一次能力描述
        await store.ensureCapabilities(axisId.value, true)
    }
}

async function handleLoadDeviceType() {
    const code = await runAction(() => ktechApi.getDeviceType(axisId.value))
    if (code !== undefined) deviceTypeCode.value = code
}

async function handleConnect() {
    await runAction(() => ktechApi.connectKtech(axisId.value), t('ktechConsole.connected'))
}
async function handleDisconnect() {
    await runAction(() => ktechApi.disconnectKtech(axisId.value), t('ktechConsole.disconnected'))
}
async function handleReboot() {
    await runAction(() => ktechApi.rebootKtech(axisId.value), t('ktechConsole.rebootSent'))
}

// ───── Calib Tab 操作 ──────────────────────────────────────────────
async function handleLoadCalib() {
    const dto = await runAction(() => ktechApi.getCalib(axisId.value), t('ktechConsole.calibLoaded'))
    if (dto) Object.assign(calibForm, dto)
}
async function handleAlignMotorEncoder() {
    await runAction(() => ktechApi.alignMotorEncoder(axisId.value), t('ktechConsole.alignTriggered'))
}
async function handleSetEncoderZero() {
    await runAction(() => ktechApi.setEncoderZero(axisId.value), t('ktechConsole.encoderZeroSet'))
}
async function handleResetCalib() {
    await runAction(() => ktechApi.resetCalib(axisId.value), t('ktechConsole.calibReset'))
}

// ───── Setting Tab 操作 ────────────────────────────────────────────
async function handleLoadSetting() {
    const dto = await runAction(() => ktechApi.getSetting(axisId.value), t('ktechConsole.settingLoaded'))
    if (dto) {
        Object.assign(settingForm, dto)
        // 同步 PID RAM 表单（Setting 用 ushort，PID RAM 入参用 byte，超出 255 时截断）
        const clamp = (v: number) => Math.min(255, Math.max(0, v | 0))
        pidRamForm.angleKp = clamp(dto.anglePidKp)
        pidRamForm.angleKi = clamp(dto.anglePidKi)
        pidRamForm.angleKd = clamp(dto.anglePidKd)
        pidRamForm.speedKp = clamp(dto.speedPidKp)
        pidRamForm.speedKi = clamp(dto.speedPidKi)
        pidRamForm.speedKd = clamp(dto.speedPidKd)
        pidRamForm.currentKp = clamp(dto.currentPidKp)
        pidRamForm.currentKi = clamp(dto.currentPidKi)
        pidRamForm.currentKd = clamp(dto.currentPidKd)
    }
}
async function handleResetSetting() {
    await runAction(() => ktechApi.resetSetting(axisId.value), t('ktechConsole.settingReset'))
}
async function handleWritePidRam() {
    await runAction(() => ktechApi.writePidRam(axisId.value, { ...pidRamForm }), t('ktechConsole.pidWritten'))
}

// ───── 参数 Tab（标定+设置 合并）操作 ───────────────────────────────
/**
 * 统一"读取"：串行拉取标定 + 设置，一次性回填表单。
 * 任一步失败不阻塞后续，由 runAction 内部 toast。
 */
async function handleLoadParams() {
    await handleLoadCalib()
    await handleLoadSetting()
}

/**
 * 统一"保存并固化"：标定 → 设置 → 固化到 ROM 三步串行。
 * 用 runAction 包装一次成功提示；任一异常被 toast 弹出后终止后续。
 */
async function handleSaveParams() {
    await runAction(async () => {
        await ktechApi.saveCalib(axisId.value, { ...calibForm })
        await ktechApi.saveSetting(axisId.value, { ...settingForm })
        await ktechApi.persistSetting(axisId.value)
    }, t('ktechConsole.paramsSaved'))
}

// ───── Motion Tab 操作 ─────────────────────────────────────────────
async function handleMotorOn() {
    await runAction(() => ktechApi.motorOn(axisId.value), t('ktechConsole.motorOn'))
}
async function handleMotorOff() {
    await runAction(() => ktechApi.motorOff(axisId.value), t('ktechConsole.motorOff'))
}
async function handleMotorStop() {
    await runAction(() => ktechApi.motorStop(axisId.value), t('ktechConsole.motorStop'))
}
async function handleMotorRestore() {
    await runAction(() => ktechApi.motorRestore(axisId.value), t('ktechConsole.motorRestore'))
}
async function handleBrakeRelease() {
    await runAction(() => ktechApi.brake(axisId.value, true), t('ktechConsole.brakeReleased'))
}
async function handleBrakeApply() {
    await runAction(() => ktechApi.brake(axisId.value, false), t('ktechConsole.brakeApplied'))
}
async function handleClearLoops() {
    await runAction(() => ktechApi.clearLoops(axisId.value), t('ktechConsole.loopsCleared'))
}
async function handleSetMotorZeroRam() {
    await runAction(() => ktechApi.setMotorZeroRam(axisId.value), t('ktechConsole.zeroSetRam'))
}
async function handleClearError() {
    await runAction(() => ktechApi.clearError(axisId.value), t('ktechConsole.errorCleared'))
}
async function handleMotion() {
    const payload: KtechMotionInputDto = { mode: motionForm.mode }
    const required = motionRequiredFields[motionForm.mode] ?? []
    // 提交前根据能力量程校验，越界则拒绝下发
    for (const key of required) {
        const value = (motionForm as Record<string, number>)[key]
        const range = fieldRange(key)
        if (range) {
            if (range.min != null && value < range.min) {
                toast.error(`${fieldLabel(key, key)} 不能小于 ${range.min}`)
                return
            }
            if (range.max != null && value > range.max) {
                toast.error(`${fieldLabel(key, key)} 不能大于 ${range.max}`)
                return
            }
        }
        ;(payload as unknown as Record<string, number | KtechMotionMode>)[key] = value
    }
    const resp = await runAction(() => ktechApi.motion(axisId.value, payload), t('ktechConsole.motionSent'))
    if (resp) {
        store.pushLog(
            'info',
            t('ktechConsole.motionLog', {
                temp: resp.motorTemperature,
                torque: resp.torqueOrPower,
                speed: resp.speed,
                enc: resp.encoderValue,
            })
        )
    }
}

// ───── 升级 ────────────────────────────────────────────────────────
function handleFileChange(e: Event) {
    const target = e.target as HTMLInputElement
    firmwareFile.value = target.files?.[0] ?? null
}

async function handleUploadFirmware() {
    if (!firmwareFile.value) {
        toast.warning(t('ktechConsole.noFirmwareFile'))
        return
    }
    uploading.value = true
    try {
        store.pushLog('info', `[升级] 开始上传 ${firmwareFile.value.name}`)
        await ktechApi.uploadFirmware(axisId.value, firmwareFile.value)
        toast.success(t('ktechConsole.upgradeComplete'))
        store.pushLog('success', '[升级] 完成')
    } catch (err) {
        const msg = (err as Error).message
        toast.error(t('ktechConsole.upgradeFailed', { msg }))
        store.pushLog('error', `[升级失败] ${msg}`)
    } finally {
        uploading.value = false
    }
}

function stageLabel(stage: KtechUpgradeStage): string {
    switch (stage) {
        case KtechUpgradeStage.Started:
            return t('ktechConsole.stageStarted')
        case KtechUpgradeStage.WaitingHandshake:
            return t('ktechConsole.stageWaiting')
        case KtechUpgradeStage.HeaderSent:
            return t('ktechConsole.stageHeaderSent')
        case KtechUpgradeStage.Transferring:
            return t('ktechConsole.stageTransferring')
        case KtechUpgradeStage.Finalizing:
            return t('ktechConsole.stageFinalizing')
        case KtechUpgradeStage.Completed:
            return t('ktechConsole.stageCompleted')
        case KtechUpgradeStage.Failed:
            return t('ktechConsole.stageFailed')
        default:
            return String(stage)
    }
}

function modeLabel(mode: KtechMotionMode): string {
    switch (mode) {
        case KtechMotionMode.Open:
            return t('ktechConsole.modeOpen')
        case KtechMotionMode.TorqueOrPower:
            return t('ktechConsole.modeTorque')
        case KtechMotionMode.Speed:
            return t('ktechConsole.modeSpeed')
        case KtechMotionMode.MultiAngle:
            return t('ktechConsole.modeMultiAngle')
        case KtechMotionMode.MultiAngleWithSpeed:
            return t('ktechConsole.modeMultiAngleSpeed')
        case KtechMotionMode.SingleAngle:
            return t('ktechConsole.modeSingleAngle')
        case KtechMotionMode.SingleAngleWithSpeed:
            return t('ktechConsole.modeSingleAngleSpeed')
        case KtechMotionMode.IncrementAngle:
            return t('ktechConsole.modeIncrAngle')
        case KtechMotionMode.IncrementAngleWithSpeed:
            return t('ktechConsole.modeIncrAngleSpeed')
        default:
            return String(mode)
    }
}

const motionModeOptions = [
    KtechMotionMode.Open,
    KtechMotionMode.TorqueOrPower,
    KtechMotionMode.Speed,
    KtechMotionMode.MultiAngle,
    KtechMotionMode.MultiAngleWithSpeed,
    KtechMotionMode.SingleAngle,
    KtechMotionMode.SingleAngleWithSpeed,
    KtechMotionMode.IncrementAngle,
    KtechMotionMode.IncrementAngleWithSpeed,
]

// ───── 设备能力联动 ─────────────────────────────────────────────────
// 切换 axisId 时通过 store.ensureCapabilities 加载并缓存能力描述；
// 模板用 isReadOnlySetting / fieldRange / fieldLabel / filteredMotionModes 派生控件状态。
const capabilities = computed(() => store.capabilitiesByAxis[axisId.value] ?? null)
const readOnlySettingSet = computed(() => new Set(capabilities.value?.readOnlySettingFields ?? []))
const readOnlyCalibSet = computed(() => new Set(capabilities.value?.readOnlyCalibFields ?? []))

function fieldRange(key: string) {
    return capabilities.value?.fieldRanges[key] ?? null
}
function fieldLabel(key: string, fallback: string) {
    return capabilities.value?.fieldLabelOverrides[key] ?? fallback
}
function isReadOnlySetting(key: string) {
    return readOnlySettingSet.value.has(key)
}
function isReadOnlyCalib(key: string) {
    return readOnlyCalibSet.value.has(key)
}

/**
 * 当前型号实际可选的运动模式列表。
 * 优先级：① 后端 capabilities.supportedControlModes ② 按 deviceTypeCode 派生（对齐瓴控 Demo FormMainSetting.cs）③ 全部模式。
 * Demo 行为：MS(0x2011) 把模式 0 显示为 "Open Control"（即 0xA0 Open）；
 * MF/MG/MG_E/MH(0x2021/0x2031/0x2032/0x2041) 模式 0 显示为 "Torque Control"（即 0xA1 TorqueOrPower）。
 */
const filteredMotionModes = computed<KtechMotionMode[]>(() => {
    const supported = capabilities.value?.supportedControlModes
    if (supported && supported.length > 0) return supported
    const code = deviceTypeCode.value ?? calibForm.deviceTypeCode
    if (code === 0x2011) {
        // MS 系列（含 MS4010）：使用 Open Control，不含 Torque
        return motionModeOptions.filter((m) => m !== KtechMotionMode.TorqueOrPower)
    }
    if (code === 0x2021 || code === 0x2031 || code === 0x2032 || code === 0x2041) {
        // MF / MG / MG_E / MH：使用 Torque Control，不含 Open
        return motionModeOptions.filter((m) => m !== KtechMotionMode.Open)
    }
    return motionModeOptions
})

// ───── 设置表单字段元数据（方便循环渲染） ─────────────────────────
// `mg` / `savedFlag` 等不参与通用循环编辑
type EditableCalibKey = Exclude<keyof KtechCalibDto, 'mg' | 'deviceTypeCode' | 'savedFlag'>
type EditableSettingKey = keyof KtechSettingDto

// 枚举型字段的"可输入下拉"建议项（对齐瓴控 Demo FormMainSetting.cs 中 ComboBox Items）
// 使用 HTML datalist 提供下拉建议，仍允许用户手输任意整数
type FieldOption = { value: number; label: string }
// Demo formSettingRefresh 语义：
// device 值 0 = 不支持（控件 Disabled，UI 应灰显）
// RS485 波特率索引（KTECH 固件存储索引值，非实际波特率）
// Demo 共 8 项（index 0~7），对应 9600/19200/38400/57600/115200/230400/460800/921600
const RS485_BAUD_OPTS: FieldOption[] = [
    { value: 0, label: '0 - 9600 bps' },
    { value: 1, label: '1 - 19200 bps' },
    { value: 2, label: '2 - 38400 bps' },
    { value: 3, label: '3 - 57600 bps' },
    { value: 4, label: '4 - 115200 bps' },
    { value: 5, label: '5 - 230400 bps' },
    { value: 6, label: '6 - 460800 bps' },
    { value: 7, label: '7 - 921600 bps' },
]
// CAN 波特率索引（KTECH 固件存储索引值，非实际波特率）
const CAN_BAUD_OPTS: FieldOption[] = [
    { value: 0, label: '100000 bps' },
    { value: 1, label: '125000 bps' },
    { value: 2, label: '250000 bps' },
    { value: 3, label: '500000 bps' },
    { value: 4, label: '1000000 bps' },
]
/** 具有"0 = 不支持"语义的字段集合，用于禁用逻辑，不依赖 label 文本 */
const ZERO_DISABLED_FIELDS = new Set<string>([
    'brakeResEnable',
    'protectMotorTempEnable',
    'protectDriverTempEnable',
    'protectUnderVoltageEnable',
    'protectOverVoltageEnable',
    'protectOverCurrentEnable',
    'protectShortCircuitEnable',
    'protectStallEnable',
    'protectLostInputEnable',
])
const fieldOptionsMap = computed<Partial<Record<EditableSettingKey | EditableCalibKey, FieldOption[]>>>(() => {
    /** 保护功能启用字段的标准选项 */
    const protectEnableOpts: FieldOption[] = [
        { value: 0, label: t('ktechConsole.optProtectNone') },
        { value: 1, label: t('ktechConsole.optProtectDisable') },
        { value: 2, label: t('ktechConsole.optProtectSlowTrigger') },
        { value: 3, label: t('ktechConsole.optProtectFastTrigger') },
    ]
    return {
        busType: [
            { value: 0, label: 'NONE' },
            { value: 1, label: 'RS485' },
            { value: 2, label: 'CAN' },
            { value: 3, label: 'EtherCAT' },
        ],
        rs485BaudRate: RS485_BAUD_OPTS,
        canBaudRate: CAN_BAUD_OPTS,
        broadcastMode: [
            { value: 0, label: 'OFF' },
            { value: 1, label: 'ON' },
        ],
        spinDirection: [
            { value: 0, label: 'Normal' },
            { value: 1, label: 'Reverse' },
        ],
        // Demo formSettingRefresh 语义：0=不支持, 1=Disable(index 0), 2=Enable(index 1)
        brakeResEnable: [
            { value: 0, label: t('ktechConsole.optBrakeResNone') },
            { value: 1, label: t('ktechConsole.optBrakeResDisable') },
            { value: 2, label: t('ktechConsole.optBrakeResEnable') },
        ],
        protectMotorTempEnable: protectEnableOpts,
        protectDriverTempEnable: protectEnableOpts,
        protectUnderVoltageEnable: protectEnableOpts,
        protectOverVoltageEnable: protectEnableOpts,
        protectOverCurrentEnable: protectEnableOpts,
        protectShortCircuitEnable: protectEnableOpts,
        protectStallEnable: protectEnableOpts,
        protectLostInputEnable: protectEnableOpts,
        // 输入类型
        inputType: [
            { value: 0, label: t('ktechConsole.optBusCan') },
            { value: 1, label: t('ktechConsole.optPwmInput') },
        ],
        pwmInputControlMode: [
            { value: 0, label: t('ktechConsole.optPwmOff') },
            { value: 1, label: t('ktechConsole.optPwmSpeedCtrl') },
            { value: 2, label: t('ktechConsole.optPwmPosCtrl') },
            { value: 3, label: t('ktechConsole.optPwmTorqueCtrl') },
        ],
        // 校准字段
        encoderPos: [
            { value: 0, label: '0 - Normal' },
            { value: 1, label: '1 - Reverse' },
        ],
        motorPhaseSequence: [
            { value: 0, label: '0 - Normal' },
            { value: 1, label: '1 - Reverse' },
        ],
        // Demo dataStreamClass.encoderType[0..9]
        encoderType: [
            { value: 0, label: '0 - AS5600' },
            { value: 1, label: '1 - AS5047P' },
            { value: 2, label: '2 - AS5048A' },
            { value: 3, label: '3 - AS5048B' },
            { value: 4, label: '4 - TLE5012B' },
            { value: 5, label: '5 - 14Bit Encoder' },
            { value: 6, label: '6 - 14Bit Encoder (v2)' },
            { value: 7, label: '7 - 18Bit Encoder' },
            { value: 8, label: '8 - 21Bit Encoder' },
            { value: 9, label: '9 - 19Bit Encoder' },
        ],
    }
})

/** 取字段对应的下拉建议项（无则返回空数组）。 */
function fieldOptions(key: string): FieldOption[] {
    return fieldOptionsMap.value[key as keyof typeof fieldOptionsMap.value] ?? []
}

/**
 * 取字段当前 value 对应的可读文字（用于"只读但有枚举"的字段渲染，如编码器类型）。
 * 未匹配到时回退到原始数字字符串。
 */
function fieldOptionLabel(key: string, value: number | null | undefined): string {
    if (value === null || value === undefined) return ''
    const opts = fieldOptions(key)
    const hit = opts.find((o) => o.value === value)
    return hit ? hit.label : String(value)
}

/** 判断字段是否有枚举下拉选项。 */
function hasFieldOptions(key: string): boolean {
    return fieldOptions(key).length > 0
}

// 设置：过流阈值 mA → A 显示提示（只读副文本）
const protectOverCurrentA = computed(() => (settingForm.protectOverCurrent / 1000).toFixed(2))

/**
 * 判断字段在当前值为 0 时是否应禁用下拉。
 * 仅对"0 表示不支持"的字段有效，通过 ZERO_DISABLED_FIELDS 集合判断，
 * 避免误禁用 SpinDirection 等正常 0 值字段。
 */
function isDisabledWhenZero(key: string): boolean {
    return ZERO_DISABLED_FIELDS.has(key)
}

/**
 * 返回下拉可见选项：
 * - 若字段不属于"0=不支持"类型，返回全部选项。
 * - 若当前值为 0（不支持），返回全部选项（select 已禁用，用于展示当前"不支持"值）。
 * - 若当前值不为 0，过滤掉 value=0 的选项，防止用户主动选择"不支持"。
 */
function selectableOptions(key: string, currentValue: unknown): FieldOption[] {
    const opts = fieldOptions(key)
    if (!isDisabledWhenZero(key) || currentValue === 0) return opts
    return opts.filter((o) => o.value !== 0)
}

const settingFields = computed<{ key: EditableSettingKey; label: string }[]>(() => [
    { key: 'driverId', label: t('ktechConsole.fieldDriverId') },
    { key: 'busType', label: t('ktechConsole.fieldBusType') },
    { key: 'rs485BaudRate', label: t('ktechConsole.fieldRs485Baud') },
    { key: 'canBaudRate', label: t('ktechConsole.fieldCanBaud') },
    { key: 'broadcastMode', label: t('ktechConsole.fieldBroadcastMode') },
    { key: 'spinDirection', label: t('ktechConsole.fieldSpinDirection') },
    { key: 'protectMotorTempEnable', label: t('ktechConsole.fieldProtectMotorTemp') },
    { key: 'protectDriverTempEnable', label: t('ktechConsole.fieldProtectDriverTemp') },
    { key: 'protectUnderVoltageEnable', label: t('ktechConsole.fieldProtectUnderVoltage') },
    { key: 'protectOverVoltageEnable', label: t('ktechConsole.fieldProtectOverVoltage') },
    { key: 'protectOverCurrentEnable', label: t('ktechConsole.fieldProtectOverCurrent') },
    { key: 'protectShortCircuitEnable', label: t('ktechConsole.fieldProtectShortCircuit') },
    { key: 'protectStallEnable', label: t('ktechConsole.fieldProtectStall') },
    { key: 'protectLostInputEnable', label: t('ktechConsole.fieldProtectLostInput') },
    { key: 'protectMotorTemp', label: t('ktechConsole.fieldProtectMotorTempVal') },
    { key: 'protectDriverTemp', label: t('ktechConsole.fieldProtectDriverTempVal') },
    { key: 'protectUnderVoltage', label: t('ktechConsole.fieldProtectUnderVoltageVal') },
    { key: 'protectOverVoltage', label: t('ktechConsole.fieldProtectOverVoltageVal') },
    { key: 'protectOverCurrent', label: t('ktechConsole.fieldProtectOverCurrentVal') },
    { key: 'protectOverCurrentTime', label: t('ktechConsole.fieldProtectOverCurrentTime') },
    { key: 'protectStallTime', label: t('ktechConsole.fieldProtectStallTime') },
    { key: 'protectLostInputTime', label: t('ktechConsole.fieldProtectLostInputTime') },
    { key: 'brakeResEnable', label: t('ktechConsole.fieldBrakeResEnable') },
    { key: 'brakeResOnVoltage', label: t('ktechConsole.fieldBrakeResVoltage') },
    { key: 'inputType', label: t('ktechConsole.fieldInputType') },
    { key: 'pwmInputControlMode', label: t('ktechConsole.fieldPwmControlMode') },
    { key: 'pwmInputMinValue', label: t('ktechConsole.fieldPwmMin') },
    { key: 'pwmInputMaxValue', label: t('ktechConsole.fieldPwmMax') },
    { key: 'pwmInputCenterValue', label: t('ktechConsole.fieldPwmCenter') },
    { key: 'pwmInputDeadband', label: t('ktechConsole.fieldPwmDeadband') },
    { key: 'pwmToTorqueRatio', label: t('ktechConsole.fieldPwmToTorqueRatio') },
    { key: 'pwmToSpeedRatio', label: t('ktechConsole.fieldPwmToSpeedRatio') },
    { key: 'pwmToAngleRatio', label: t('ktechConsole.fieldPwmToAngleRatio') },
    { key: 'pulsesPerCircle', label: t('ktechConsole.fieldPulsesPerCircle') },
    { key: 'anglePidKp', label: t('ktechConsole.fieldAnglePidKp') },
    { key: 'anglePidKi', label: t('ktechConsole.fieldAnglePidKi') },
    { key: 'anglePidKd', label: t('ktechConsole.fieldAnglePidKd') },
    { key: 'speedPidKp', label: t('ktechConsole.fieldSpeedPidKp') },
    { key: 'speedPidKi', label: t('ktechConsole.fieldSpeedPidKi') },
    { key: 'speedPidKd', label: t('ktechConsole.fieldSpeedPidKd') },
    { key: 'currentPidKp', label: t('ktechConsole.fieldCurrentPidKp') },
    { key: 'currentPidKi', label: t('ktechConsole.fieldCurrentPidKi') },
    { key: 'currentPidKd', label: t('ktechConsole.fieldCurrentPidKd') },
    { key: 'maxTorque', label: t('ktechConsole.fieldMaxTorque') },
    { key: 'maxSpeed', label: t('ktechConsole.fieldMaxSpeed') },
    { key: 'maxAngle', label: t('ktechConsole.fieldMaxAngle') },
    { key: 'currentRamp', label: t('ktechConsole.fieldCurrentRamp') },
    { key: 'speedRamp', label: t('ktechConsole.fieldSpeedRamp') },
    { key: 'uniqueId', label: t('ktechConsole.fieldUniqueId') },
    { key: 'savedFlag', label: t('ktechConsole.fieldSavedFlag') },
])

const calibFields = computed<{ key: EditableCalibKey; label: string }[]>(() => [
    { key: 'motorPoles', label: t('ktechConsole.calibMotorPoles') },
    { key: 'encoderType', label: t('ktechConsole.calibEncoderType') },
    { key: 'encoderPos', label: t('ktechConsole.calibEncoderPos') },
    { key: 'motorPhaseSequence', label: t('ktechConsole.calibMotorPhaseSeq') },
    { key: 'motorEncoderAlignBias', label: t('ktechConsole.calibEncoderAlignBias') },
    { key: 'motorEncoderAlignRatio', label: t('ktechConsole.calibEncoderAlignRatio') },
    { key: 'motorEncoderAlignVoltage', label: t('ktechConsole.calibEncoderAlignVoltage') },
    { key: 'motorEncoderAlignFlag', label: t('ktechConsole.calibEncoderAlignFlag') },
    { key: 'encoderOffset', label: t('ktechConsole.calibEncoderOffset') },
    { key: 'encoderOffsetFlag', label: t('ktechConsole.calibEncoderOffsetFlag') },
])

// MG/MG_E 专有字段（动态显示）
type MgEditableKey = Exclude<keyof NonNullable<KtechCalibDto['mg']>, 'alignValueList'>
const mgFields = computed<{ key: MgEditableKey; label: string }[]>(() => [
    { key: 'reductionRatio', label: t('ktechConsole.mgReductionRatio') },
    { key: 'encoderRelateValue', label: t('ktechConsole.mgEncoderRelateValue') },
    { key: 'encoderRelateValueFlag', label: t('ktechConsole.mgEncoderRelateValueFlag') },
    { key: 'encoder2Offset', label: t('ktechConsole.mgEncoder2Offset') },
    { key: 'encoder2OffsetFlag', label: t('ktechConsole.mgEncoder2OffsetFlag') },
])

// ───── 生命周期 ────────────────────────────────────────────────────
onMounted(async () => {
    await store.acquireHub()
    // 加载设备能力（用于 UI 禁用 / 量程限定，不触发硬件读取）
    await store.ensureCapabilities(axisId.value)
    // 初始化采样开关状态（与后端同步）
    try {
        isSamplingEnabled.value = await ktechApi.getSamplingEnabled(axisId.value)
    } catch {
        /* 读取失败则保持默认 true */
    }
    // 首次进入自动尝试读一下产品信息（失败也无所谓）
    try {
        await handleLoadProductInfo()
    } catch {
        /* ignore */
    }
})

onBeforeUnmount(async () => {
    await store.releaseHub()
})

watch(axisId, async (newId) => {
    productInfo.value = null
    deviceTypeCode.value = null
    if (newId) {
        await store.ensureCapabilities(newId)
    }
})

// 当能力加载完成且当前所选模式不在支持列表中时，自动切到第一支持模式。
watch(
    () => filteredMotionModes.value,
    (modes) => {
        if (modes.length === 0) return
        if (!modes.includes(motionForm.mode)) {
            motionForm.mode = modes[0]
        }
    }
)

/**
 * 模式与所需运动字段的映射（与 handleMotion 中的派送逻辑一致）。
 * 切换模式时，仅保留该模式需要的字段，其余重置为 0，避免提交脏值。
 */
const motionRequiredFields: Record<KtechMotionMode, (keyof KtechMotionInputDto)[]> = {
    [KtechMotionMode.Open]: ['voltage'],
    [KtechMotionMode.TorqueOrPower]: ['torqueOrPower'],
    [KtechMotionMode.Speed]: ['speedSignedDps'],
    [KtechMotionMode.MultiAngle]: ['angleDeg'],
    [KtechMotionMode.MultiAngleWithSpeed]: ['angleDeg', 'speedDps'],
    [KtechMotionMode.SingleAngle]: ['singleAngleDeg', 'direction'],
    [KtechMotionMode.SingleAngleWithSpeed]: ['singleAngleDeg', 'direction', 'speedDps'],
    [KtechMotionMode.IncrementAngle]: ['angleDeg'],
    [KtechMotionMode.IncrementAngleWithSpeed]: ['angleDeg', 'speedDps'],
}

const allMotionFields: (keyof KtechMotionInputDto)[] = [
    'voltage',
    'torqueOrPower',
    'speedDps',
    'speedSignedDps',
    'angleDeg',
    'singleAngleDeg',
    'direction',
]

watch(
    () => motionForm.mode,
    (newMode) => {
        const required = new Set<keyof KtechMotionInputDto>(motionRequiredFields[newMode] ?? [])
        for (const k of allMotionFields) {
            if (!required.has(k)) {
                ;(motionForm as Record<string, number>)[k] = 0
            }
        }
    }
)

function goBack() {
    router.back()
}
</script>

<template>
    <div class="p-4 space-y-4">
        <!-- 顶部：返回 + 标题 + 实时状态条 -->
        <div class="flex items-center gap-3">
            <Button severity="secondary" outlined size="small" @click="goBack">
                <ArrowLeft class="size-4" />
                {{ t('ktechConsole.back') }}
            </Button>
            <div>
                <h1 class="text-lg font-semibold">{{ t('ktechConsole.title') }}</h1>
                <div class="text-xs text-muted-foreground">
                    {{ t('ktechConsole.axisId') }}
                    <span class="font-mono">{{ axisId }}</span>
                </div>
            </div>
            <span class="ml-auto text-xs" :class="store.hubConnected ? 'text-green-600' : 'text-red-500'">
                SignalR：{{ store.hubConnected ? t('ktechConsole.hubConnected') : t('ktechConsole.hubDisconnected') }}
            </span>
        </div>

        <!-- 实时状态摘要 -->
        <AppCard :beam="false">
            <div class="p-3">
                <!-- 采样开关 -->
                <div class="mb-2 flex items-center gap-2">
                    <ToggleSwitch v-model="isSamplingEnabled" @change="toggleSampling" />
                    <span class="select-none text-xs">{{ t('ktechConsole.realtimeSampling') }}</span>
                    <span
                        v-if="!isSamplingEnabled"
                        class="ml-1 rounded bg-yellow-100 px-1.5 text-xs text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300"
                    >
                        {{ t('ktechConsole.paused') }}
                    </span>
                </div>

                <div v-if="!liveState" class="text-sm text-muted-foreground">{{ t('ktechConsole.noStatePush') }}</div>
                <div v-else class="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4 lg:grid-cols-8">
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldTemp') }}</div>
                        <div class="font-mono">{{ liveState.motorTemperature }}°C</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldBusVoltage') }}</div>
                        <div class="font-mono">{{ liveState.busVoltage.toFixed(2) }}V</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldBusCurrent') }}</div>
                        <div class="font-mono">{{ liveState.busCurrent.toFixed(2) }}A</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldTorquePower') }}</div>
                        <div class="font-mono">{{ liveState.torqueOrPower }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldSpeed') }}</div>
                        <div class="font-mono">{{ liveState.speed }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldEncoder') }}</div>
                        <div class="font-mono">{{ liveState.encoderValue }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldMultiAngle') }}</div>
                        <div class="font-mono">{{ liveState.multiTurnAngle.toFixed(2) }}°</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('ktechConsole.fieldSingleAngle') }}</div>
                        <div class="font-mono">{{ (liveState.singleTurnAngleCentideg / 100).toFixed(2) }}°</div>
                    </div>
                </div>
                <div v-if="liveState && liveState.errorFlags.raw !== 0" class="mt-2 flex flex-wrap gap-1 text-xs">
                    <span
                        v-if="liveState.errorFlags.underVoltage"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errUnderVoltage') }}
                    </span>
                    <span
                        v-if="liveState.errorFlags.overVoltage"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errOverVoltage') }}
                    </span>
                    <span
                        v-if="liveState.errorFlags.driverOverTemp"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errDriverOverTemp') }}
                    </span>
                    <span
                        v-if="liveState.errorFlags.motorOverTemp"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errMotorOverTemp') }}
                    </span>
                    <span
                        v-if="liveState.errorFlags.overCurrent"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errOverCurrent') }}
                    </span>
                    <span
                        v-if="liveState.errorFlags.shortCircuit"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errShortCircuit') }}
                    </span>
                    <span
                        v-if="liveState.errorFlags.stall"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errStall') }}
                    </span>
                    <span
                        v-if="liveState.errorFlags.lostInput"
                        class="bg-red-100 text-red-700 rounded px-1.5 dark:bg-red-900/40 dark:text-red-300"
                    >
                        {{ t('ktechConsole.errLostInput') }}
                    </span>
                </div>
            </div>
        </AppCard>

        <!-- Tab 切换按钮组 -->
        <div class="flex flex-wrap gap-2 border-b">
            <button
                v-for="tab in [
                    { key: 'info', label: t('ktechConsole.tabInfo') },
                    { key: 'params', label: t('ktechConsole.tabParams') },
                    { key: 'motion', label: t('ktechConsole.tabMotion') },
                    { key: 'log', label: t('ktechConsole.tabLog') },
                ]"
                :key="tab.key"
                :class="[
                    'px-4 py-2 text-sm border-b-2 transition-colors',
                    activeTab === tab.key
                        ? 'border-primary text-primary font-medium'
                        : 'border-transparent text-muted-foreground hover:text-foreground',
                ]"
                @click="activeTab = tab.key as any"
            >
                {{ tab.label }}
            </button>
        </div>

        <!-- ──────── 产品信息 Tab ──────── -->
        <AppCard v-if="activeTab === 'info'" :beam="false">
            <div class="p-4 space-y-4">
                <div class="flex flex-wrap gap-2">
                    <Button size="small" :disabled="acting" @click="handleLoadProductInfo">
                        <RefreshCw class="size-4" />
                        {{ t('ktechConsole.btnLoadProductInfo') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleLoadDeviceType">
                        {{ t('ktechConsole.btnDeviceType') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleConnect">
                        <Power class="size-4" />
                        {{ t('ktechConsole.btnConnect') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleDisconnect">
                        <PowerOff class="size-4" />
                        {{ t('ktechConsole.btnDisconnect') }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        class="text-orange-500"
                        :disabled="acting"
                        @click="handleReboot"
                    >
                        {{ t('ktechConsole.btnReboot') }}
                    </Button>
                </div>
                <div v-if="deviceTypeCode !== null" class="text-sm">
                    {{ t('ktechConsole.deviceTypeCode') }}
                    <span class="font-mono">0x{{ deviceTypeCode.toString(16).toUpperCase() }}</span>
                </div>
                <div v-if="productInfo" class="grid grid-cols-2 gap-3 text-sm">
                    <div>
                        <div class="text-muted-foreground text-xs">{{ t('ktechConsole.fieldDeviceType') }}</div>
                        <div class="font-mono">
                            {{ productInfo.deviceTypeName }} (0x{{ productInfo.deviceTypeCode.toString(16) }})
                        </div>
                    </div>
                    <div>
                        <div class="text-muted-foreground text-xs">{{ t('ktechConsole.fieldDriver') }}</div>
                        <div class="font-mono">{{ productInfo.driverName }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground text-xs">{{ t('ktechConsole.fieldMotor') }}</div>
                        <div class="font-mono">{{ productInfo.motorName }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground text-xs">{{ t('ktechConsole.fieldChipId') }}</div>
                        <div class="font-mono break-all">{{ productInfo.chipId }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground text-xs">{{ t('ktechConsole.fieldHwVersion') }}</div>
                        <div class="font-mono">{{ productInfo.hardwareVersion }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground text-xs">{{ t('ktechConsole.fieldMotorVersion') }}</div>
                        <div class="font-mono">{{ productInfo.motorVersion }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground text-xs">{{ t('ktechConsole.fieldFwVersion') }}</div>
                        <div class="font-mono">{{ productInfo.firmwareVersion }}</div>
                    </div>
                </div>
                <div v-else class="text-sm text-muted-foreground">{{ t('ktechConsole.noProductInfo') }}</div>
            </div>
        </AppCard>

        <!-- ──────── 参数 Tab（标定 + 设置 合并） ──────── -->
        <AppCard v-if="activeTab === 'params'" :beam="false">
            <div class="p-4 space-y-6">
                <!-- 顶部统一按钮区 -->
                <div class="flex flex-wrap gap-2">
                    <Button size="small" :disabled="acting" @click="handleLoadParams">
                        <RefreshCw class="size-4" />
                        {{ t('ktechConsole.btnLoadParams') }}
                    </Button>
                    <Button
                        size="small"
                        class="bg-blue-600 text-white hover:bg-blue-700"
                        :disabled="acting"
                        @click="handleSaveParams"
                    >
                        {{ t('ktechConsole.btnSaveParams') }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="acting"
                        @click="handleAlignMotorEncoder"
                    >
                        {{ t('ktechConsole.btnAlignEncoder') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleSetEncoderZero">
                        {{ t('ktechConsole.btnSetEncoderZero') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleResetCalib">
                        {{ t('ktechConsole.btnResetCalib') }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        class="text-orange-500"
                        :disabled="acting"
                        @click="handleResetSetting"
                    >
                        {{ t('ktechConsole.btnResetFactory') }}
                    </Button>
                </div>

                <!-- ─── 标定字段区 ─── -->
                <section class="space-y-3">
                    <h3 class="text-sm font-semibold border-l-4 border-primary pl-2">
                        {{ t('ktechConsole.calibSection') }}
                    </h3>
                    <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
                        <!-- 普通字段（含枚举下拉） -->
                        <label v-for="f in calibFields" :key="f.key" class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ fieldLabel(f.key, f.label) }}</span>
                            <!-- 有枚举 + 只读：显示中文标签 -->
                            <InputText
                                v-if="hasFieldOptions(f.key) && isReadOnlyCalib(f.key)"
                                :value="fieldOptionLabel(f.key, calibForm[f.key] as number)"
                                size="small"
                                class="!text-xs opacity-70 cursor-not-allowed"
                                readonly
                            />
                            <!-- 有枚举 + 可写：下拉选择 -->
                            <Select
                                v-else-if="hasFieldOptions(f.key)"
                                v-model="(calibForm as any)[f.key]"
                                :options="fieldOptions(f.key)"
                                option-label="label"
                                option-value="value"
                                size="small"
                                class="!text-xs"
                                :pt="{
                                    root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                    label: {
                                        class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                    },
                                    dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                                }"
                            />
                            <!-- 数字字段 -->
                            <InputNumber
                                v-else
                                v-model="(calibForm as any)[f.key]"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :disabled="isReadOnlyCalib(f.key)"
                                :min="fieldRange(f.key)?.min ?? undefined"
                                :max="fieldRange(f.key)?.max ?? undefined"
                                :step="fieldRange(f.key)?.step ?? undefined"
                            />
                        </label>
                    </div>
                    <!-- MG / MG_E 扩展字段 -->
                    <template v-if="isMgVariant && calibForm.mg">
                        <div class="mt-2 border-t pt-3 space-y-3">
                            <div class="text-xs font-semibold text-muted-foreground">
                                {{ t('ktechConsole.mgSection') }}
                            </div>
                            <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
                                <label v-for="f in mgFields" :key="f.key" class="flex flex-col gap-1 text-xs">
                                    <span class="text-muted-foreground">{{ f.label }}</span>
                                    <InputNumber
                                        v-model="(calibForm.mg as any)[f.key]"
                                        size="small"
                                        fluid
                                        input-class="!text-xs"
                                        :use-grouping="false"
                                    />
                                </label>
                            </div>
                            <div class="space-y-2">
                                <div class="text-xs text-muted-foreground">{{ t('ktechConsole.alignValueList') }}</div>
                                <div class="grid grid-cols-4 md:grid-cols-8 gap-1.5">
                                    <InputNumber
                                        v-for="(_, i) in calibForm.mg!.alignValueList"
                                        :key="i"
                                        v-model="calibForm.mg!.alignValueList[i]"
                                        size="small"
                                        fluid
                                        input-class="!text-xs"
                                        :use-grouping="false"
                                    />
                                </div>
                            </div>
                        </div>
                    </template>
                </section>

                <!-- ─── 设置字段区 ─── -->
                <section class="space-y-3">
                    <h3 class="text-sm font-semibold border-l-4 border-primary pl-2">
                        {{ t('ktechConsole.settingSection') }}
                    </h3>
                    <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
                        <label v-for="f in settingFields" :key="f.key" class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">
                                {{ fieldLabel(f.key, f.label) }}
                                <span v-if="fieldRange(f.key)?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange(f.key)?.unit }})
                                </span>
                            </span>
                            <!-- 有枚举 + 只读 -->
                            <InputText
                                v-if="hasFieldOptions(f.key) && isReadOnlySetting(f.key)"
                                :value="fieldOptionLabel(f.key, settingForm[f.key] as number)"
                                size="small"
                                class="!text-xs opacity-70 cursor-not-allowed"
                                readonly
                            />
                            <!-- 有枚举 + 可写；protect enable 值为 0（不支持）时禁用 -->
                            <Select
                                v-else-if="hasFieldOptions(f.key)"
                                v-model="(settingForm as any)[f.key]"
                                :options="selectableOptions(f.key, (settingForm as any)[f.key])"
                                option-label="label"
                                option-value="value"
                                size="small"
                                class="!text-xs"
                                :disabled="isDisabledWhenZero(f.key) && (settingForm as any)[f.key] === 0"
                                :pt="{
                                    root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                    label: {
                                        class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                    },
                                    dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                                }"
                            />
                            <!-- 数字字段 -->
                            <InputNumber
                                v-else
                                v-model="(settingForm as any)[f.key]"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :disabled="isReadOnlySetting(f.key)"
                                :min="fieldRange(f.key)?.min ?? undefined"
                                :max="fieldRange(f.key)?.max ?? undefined"
                                :step="fieldRange(f.key)?.step ?? undefined"
                            />
                            <!-- 过流附加 A 提示 -->
                            <span v-if="f.key === 'protectOverCurrent'" class="text-[10px] text-muted-foreground">
                                ≈ {{ protectOverCurrentA }} A
                            </span>
                        </label>
                    </div>
                </section>

                <!-- ─── PID 写入 RAM 子块 ─── -->
                <section class="border-t pt-3">
                    <h3 class="mb-2 text-sm font-medium">{{ t('ktechConsole.pidRamSection') }}</h3>
                    <div class="grid grid-cols-3 gap-3">
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidAngleKp') }}</span>
                            <InputNumber
                                v-model="pidRamForm.angleKp"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidAngleKi') }}</span>
                            <InputNumber
                                v-model="pidRamForm.angleKi"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidAngleKd') }}</span>
                            <InputNumber
                                v-model="pidRamForm.angleKd"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidSpeedKp') }}</span>
                            <InputNumber
                                v-model="pidRamForm.speedKp"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidSpeedKi') }}</span>
                            <InputNumber
                                v-model="pidRamForm.speedKi"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidSpeedKd') }}</span>
                            <InputNumber
                                v-model="pidRamForm.speedKd"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidCurrentKp') }}</span>
                            <InputNumber
                                v-model="pidRamForm.currentKp"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidCurrentKi') }}</span>
                            <InputNumber
                                v-model="pidRamForm.currentKi"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidCurrentKd') }}</span>
                            <InputNumber
                                v-model="pidRamForm.currentKd"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                            />
                        </label>
                    </div>
                    <Button class="mt-3" size="small" :disabled="acting" @click="handleWritePidRam">
                        {{ t('ktechConsole.btnWritePidRam') }}
                    </Button>
                </section>
            </div>
        </AppCard>

        <!-- ──────── 运动控制 Tab ──────── -->
        <AppCard v-if="activeTab === 'motion'" :beam="false">
            <div class="p-4 space-y-4">
                <!-- 基础动作 -->
                <div class="flex flex-wrap gap-2">
                    <Button
                        size="small"
                        class="bg-green-600 text-white hover:bg-green-700"
                        :disabled="acting"
                        @click="handleMotorOn"
                    >
                        <Power class="size-4" />
                        {{ t('ktechConsole.btnMotorOn') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleMotorOff">
                        <PowerOff class="size-4" />
                        {{ t('ktechConsole.btnMotorOff') }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        class="text-red-600"
                        :disabled="acting"
                        @click="handleMotorStop"
                    >
                        <CircleStop class="size-4" />
                        {{ t('ktechConsole.btnMotorStop') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleMotorRestore">
                        {{ t('ktechConsole.btnMotorRestore') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleBrakeRelease">
                        {{ t('ktechConsole.btnBrakeRelease') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleBrakeApply">
                        {{ t('ktechConsole.btnBrakeApply') }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="acting" @click="handleClearLoops">
                        {{ t('ktechConsole.btnClearLoops') }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="acting"
                        @click="handleSetMotorZeroRam"
                    >
                        {{ t('ktechConsole.btnSetZeroRam') }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        class="text-orange-500"
                        :disabled="acting"
                        @click="handleClearError"
                    >
                        {{ t('ktechConsole.btnClearError') }}
                    </Button>
                </div>

                <!-- 运动模式选择 + 参数 -->
                <div class="border-t pt-4 space-y-3">
                    <div class="flex items-center gap-3">
                        <label class="text-sm">{{ t('ktechConsole.motionMode') }}</label>
                        <Select
                            v-model="motionForm.mode"
                            :options="filteredMotionModes.map((m) => ({ label: modeLabel(m), value: m }))"
                            option-label="label"
                            option-value="value"
                            size="small"
                            class="!text-xs w-52"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                    </div>

                    <div class="grid grid-cols-2 md:grid-cols-4 gap-3">
                        <label v-if="motionForm.mode === KtechMotionMode.Open" class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">
                                {{ fieldLabel('voltage', t('ktechConsole.fieldVoltage')) }}
                                <span v-if="fieldRange('voltage')?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange('voltage')?.unit }})
                                </span>
                            </span>
                            <InputNumber
                                v-model="motionForm.voltage"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :min="fieldRange('voltage')?.min ?? undefined"
                                :max="fieldRange('voltage')?.max ?? undefined"
                                :step="fieldRange('voltage')?.step ?? undefined"
                            />
                        </label>
                        <label
                            v-if="motionForm.mode === KtechMotionMode.TorqueOrPower"
                            class="flex flex-col gap-1 text-xs"
                        >
                            <span class="text-muted-foreground">
                                {{ fieldLabel('torqueOrPower', t('ktechConsole.fieldTorquePower')) }}
                                <span v-if="fieldRange('torqueOrPower')?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange('torqueOrPower')?.unit }})
                                </span>
                            </span>
                            <InputNumber
                                v-model="motionForm.torqueOrPower"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :min="fieldRange('torqueOrPower')?.min ?? undefined"
                                :max="fieldRange('torqueOrPower')?.max ?? undefined"
                                :step="fieldRange('torqueOrPower')?.step ?? undefined"
                            />
                        </label>
                        <label v-if="motionForm.mode === KtechMotionMode.Speed" class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">
                                {{ fieldLabel('speedSignedDps', t('ktechConsole.fieldSpeedSigned')) }}
                                <span v-if="fieldRange('speedSignedDps')?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange('speedSignedDps')?.unit }})
                                </span>
                            </span>
                            <InputNumber
                                v-model="motionForm.speedSignedDps"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :min="fieldRange('speedSignedDps')?.min ?? undefined"
                                :max="fieldRange('speedSignedDps')?.max ?? undefined"
                                :step="fieldRange('speedSignedDps')?.step ?? undefined"
                            />
                        </label>
                        <label
                            v-if="
                                [
                                    KtechMotionMode.MultiAngle,
                                    KtechMotionMode.MultiAngleWithSpeed,
                                    KtechMotionMode.IncrementAngle,
                                    KtechMotionMode.IncrementAngleWithSpeed,
                                ].includes(motionForm.mode)
                            "
                            class="flex flex-col gap-1 text-xs"
                        >
                            <span class="text-muted-foreground">
                                {{ fieldLabel('angleDeg', t('ktechConsole.fieldAngle')) }}
                                <span v-if="fieldRange('angleDeg')?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange('angleDeg')?.unit }})
                                </span>
                            </span>
                            <InputNumber
                                v-model="motionForm.angleDeg"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :min="fieldRange('angleDeg')?.min ?? undefined"
                                :max="fieldRange('angleDeg')?.max ?? undefined"
                                :step="fieldRange('angleDeg')?.step ?? undefined"
                            />
                        </label>
                        <label
                            v-if="
                                [KtechMotionMode.SingleAngle, KtechMotionMode.SingleAngleWithSpeed].includes(
                                    motionForm.mode
                                )
                            "
                            class="flex flex-col gap-1 text-xs"
                        >
                            <span class="text-muted-foreground">
                                {{ fieldLabel('singleAngleDeg', t('ktechConsole.fieldSingleAngle')) }}
                                <span v-if="fieldRange('singleAngleDeg')?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange('singleAngleDeg')?.unit }})
                                </span>
                            </span>
                            <InputNumber
                                v-model="motionForm.singleAngleDeg"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :min="fieldRange('singleAngleDeg')?.min ?? undefined"
                                :max="fieldRange('singleAngleDeg')?.max ?? undefined"
                                :step="fieldRange('singleAngleDeg')?.step ?? undefined"
                            />
                        </label>
                        <label
                            v-if="
                                [KtechMotionMode.SingleAngle, KtechMotionMode.SingleAngleWithSpeed].includes(
                                    motionForm.mode
                                )
                            "
                            class="flex flex-col gap-1 text-xs"
                        >
                            <span class="text-muted-foreground">
                                {{ fieldLabel('direction', t('ktechConsole.fieldDirection')) }}
                            </span>
                            <Select
                                v-model="motionForm.direction"
                                :options="[
                                    { label: '0 - 正向 (Forward)', value: 0 },
                                    { label: '1 - 反向 (Rev)', value: 1 },
                                ]"
                                option-label="label"
                                option-value="value"
                                size="small"
                                class="!text-xs"
                                :pt="{
                                    root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                    label: {
                                        class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                    },
                                    dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                                }"
                            />
                        </label>
                        <label
                            v-if="
                                [
                                    KtechMotionMode.MultiAngleWithSpeed,
                                    KtechMotionMode.SingleAngleWithSpeed,
                                    KtechMotionMode.IncrementAngleWithSpeed,
                                ].includes(motionForm.mode)
                            "
                            class="flex flex-col gap-1 text-xs"
                        >
                            <span class="text-muted-foreground">
                                {{ fieldLabel('speedDps', t('ktechConsole.fieldSpeedUnsigned')) }}
                                <span v-if="fieldRange('speedDps')?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange('speedDps')?.unit }})
                                </span>
                            </span>
                            <InputNumber
                                v-model="motionForm.speedDps"
                                size="small"
                                fluid
                                input-class="!text-xs"
                                :use-grouping="false"
                                :min="fieldRange('speedDps')?.min ?? undefined"
                                :max="fieldRange('speedDps')?.max ?? undefined"
                                :step="fieldRange('speedDps')?.step ?? undefined"
                            />
                        </label>
                    </div>

                    <Button
                        size="small"
                        class="bg-blue-600 text-white hover:bg-blue-700"
                        :disabled="acting"
                        @click="handleMotion"
                    >
                        {{ t('ktechConsole.btnSendMotion') }}
                    </Button>
                </div>

                <!-- 固件升级 -->
                <div class="border-t pt-4 space-y-3">
                    <h3 class="text-sm font-medium">{{ t('ktechConsole.firmwareSection') }}</h3>
                    <div class="flex items-center gap-3">
                        <!-- 隐藏的原生文件输入，由 Button 触发 -->
                        <input
                            ref="firmwareInputRef"
                            type="file"
                            accept=".bin,.hex,.kf"
                            class="hidden"
                            :disabled="uploading"
                            @change="handleFileChange"
                        />
                        <Button
                            severity="secondary"
                            size="small"
                            outlined
                            class="!h-7 !px-2 !text-xs"
                            :disabled="uploading"
                            @click="firmwareInputRef?.click()"
                        >
                            {{ t('ktechConsole.selectFirmware') }}
                        </Button>
                        <span class="text-xs text-muted-foreground truncate max-w-[12rem]">
                            {{ firmwareFileName ?? t('ktechConsole.noFileSelected') }}
                        </span>
                        <Button
                            size="small"
                            class="bg-purple-600 text-white hover:bg-purple-700"
                            :disabled="uploading || !firmwareFile"
                            @click="handleUploadFirmware"
                        >
                            <Upload class="size-4" />
                            {{ uploading ? t('ktechConsole.upgrading') : t('ktechConsole.startUpgrade') }}
                        </Button>
                    </div>
                    <div
                        v-if="upgradeLog.length > 0"
                        class="rounded border bg-muted/20 p-2 max-h-48 overflow-auto text-xs space-y-1"
                    >
                        <div v-for="(p, idx) in upgradeLog" :key="idx" class="flex items-center gap-2 font-mono">
                            <span
                                class="shrink-0 w-20"
                                :class="p.stage === KtechUpgradeStage.Failed ? 'text-red-500' : 'text-muted-foreground'"
                            >
                                [{{ stageLabel(p.stage) }}]
                            </span>
                            <span class="shrink-0 w-12">{{ p.percent }}%</span>
                            <span class="shrink-0 w-32">{{ p.bytesSent }} / {{ p.totalBytes }}</span>
                            <span class="flex-1 truncate">{{ p.message ?? '' }}</span>
                        </div>
                    </div>
                </div>
            </div>
        </AppCard>

        <!-- ──────── 日志 Tab ──────── -->
        <AppCard v-if="activeTab === 'log'" :beam="false">
            <div class="p-4 space-y-2">
                <div class="flex items-center gap-2">
                    <h3 class="text-sm font-medium">{{ t('ktechConsole.logTitle', { count: store.logs.length }) }}</h3>
                    <Button size="small" severity="secondary" outlined @click="store.logs.splice(0)">
                        <Trash2 class="size-3.5" />
                        {{ t('ktechConsole.logClear') }}
                    </Button>
                </div>
                <div class="rounded border bg-muted/20 p-2 max-h-[60vh] overflow-auto text-xs space-y-1 font-mono">
                    <div v-for="(l, idx) in store.logs" :key="idx" class="flex items-start gap-2">
                        <span class="shrink-0 text-muted-foreground">{{ l.time.substring(11, 23) }}</span>
                        <span
                            class="shrink-0 w-14"
                            :class="{
                                'text-blue-600': l.level === 'info',
                                'text-green-600': l.level === 'success',
                                'text-orange-500': l.level === 'warn',
                                'text-red-600': l.level === 'error',
                            }"
                        >
                            [{{ l.level.toUpperCase() }}]
                        </span>
                        <span class="flex-1 break-all">{{ l.text }}</span>
                    </div>
                    <div v-if="store.logs.length === 0" class="text-muted-foreground">
                        {{ t('ktechConsole.logEmpty') }}
                    </div>
                </div>
            </div>
        </AppCard>
    </div>
</template>
