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
import { toast } from 'vue-sonner'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
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
const uploading = ref(false)

// ───── 计算属性：实时状态 ───────────────────────────────────────────
const liveState = computed(() => store.stateByAxis[axisId.value] ?? null)
const upgradeLog = computed(() => store.upgradeLogByAxis[axisId.value] ?? [])

// ───── 实时采样开关 ─────────────────────────────────────────────────
/** true=采样启用（默认），false=已暂停 */
const isSamplingEnabled = ref(true)

/** 切换实时采样开关，调用后端 API 同步状态 */
async function toggleSampling(): Promise<void> {
    const id = axisId.value
    if (!id) return
    try {
        if (isSamplingEnabled.value) {
            await ktechApi.disableSampling(id)
        } else {
            await ktechApi.enableSampling(id)
        }
        isSamplingEnabled.value = !isSamplingEnabled.value
    } catch (err) {
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
// device 值 1 → comboBox index 0 (Disable)
// device 值 2 → comboBox index 1 (Slow)
// device 值 3 → comboBox index 2 (Fast)
const PROTECT_ENABLE_OPTS: FieldOption[] = [
    { value: 0, label: '0 - 不支持（设备无此保护）' },
    { value: 1, label: '1 - 禁用 Disable' },
    { value: 2, label: '2 - 慢速触发 Slow' },
    { value: 3, label: '3 - 快速触发 Fast' },
]
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
const fieldOptionsMap: Partial<Record<EditableSettingKey | EditableCalibKey, FieldOption[]>> = {
    busType: [
        { value: 0, label: 'NONE' },
        { value: 1, label: 'RS485' },
        { value: 2, label: 'CAN' },
        { value: 3, label: 'EtherCAT' },
    ],
    rs485BaudRate: RS485_BAUD_OPTS,
    canBaudRate: CAN_BAUD_OPTS,
    broadcastMode: [
        { value: 0, label: 'OFF 关闭' },
        { value: 1, label: 'ON 开启' },
    ],
    spinDirection: [
        { value: 0, label: 'Normal 正向' },
        { value: 1, label: 'Reverse 反向' },
    ],
    // Demo formSettingRefresh 语义：0=不支持, 1=Disable(index 0), 2=Enable(index 1)
    brakeResEnable: [
        { value: 0, label: '0 - 不支持（设备无制动电阻）' },
        { value: 1, label: '1 - 禁用 Disable' },
        { value: 2, label: '2 - 启用 Enable' },
    ],
    protectMotorTempEnable: PROTECT_ENABLE_OPTS,
    protectDriverTempEnable: PROTECT_ENABLE_OPTS,
    protectUnderVoltageEnable: PROTECT_ENABLE_OPTS,
    protectOverVoltageEnable: PROTECT_ENABLE_OPTS,
    protectOverCurrentEnable: PROTECT_ENABLE_OPTS,
    protectShortCircuitEnable: PROTECT_ENABLE_OPTS,
    protectStallEnable: PROTECT_ENABLE_OPTS,
    protectLostInputEnable: PROTECT_ENABLE_OPTS,
    // 输入类型
    inputType: [
        { value: 0, label: '0 - 总线(RS485/CAN)' },
        { value: 1, label: '1 - PWM 输入' },
    ],
    pwmInputControlMode: [
        { value: 0, label: '0 - 关闭' },
        { value: 1, label: '1 - 速度控制' },
        { value: 2, label: '2 - 位置控制' },
        { value: 3, label: '3 - 力矩控制' },
    ],
    // 校准字段
    encoderPos: [
        { value: 0, label: '0 正向 Normal' },
        { value: 1, label: '1 反向 Reverse' },
    ],
    motorPhaseSequence: [
        { value: 0, label: '0 正向 Normal' },
        { value: 1, label: '1 反向 Reverse' },
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

/** 取字段对应的下拉建议项（无则返回空数组）。 */
function fieldOptions(key: string): FieldOption[] {
    return fieldOptionsMap[key as keyof typeof fieldOptionsMap] ?? []
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
 * 仅对"0 表示不支持"的字段有效（检查 0 选项 label 是否含"不支持"），
 * 避免误禁用 SpinDirection 等正常 0 值字段。
 */
function isDisabledWhenZero(key: string): boolean {
    const zeroOpt = fieldOptions(key).find((o) => o.value === 0)
    return !!zeroOpt && zeroOpt.label.includes('不支持')
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

const settingFields: { key: EditableSettingKey; label: string }[] = [
    { key: 'driverId', label: '驱动 ID' },
    { key: 'busType', label: '总线类型' },
    { key: 'rs485BaudRate', label: 'RS485 波特率' },
    { key: 'canBaudRate', label: 'CAN 波特率' },
    { key: 'broadcastMode', label: '广播模式' },
    { key: 'spinDirection', label: '旋转方向' },
    { key: 'protectMotorTempEnable', label: '电机温度保护启用' },
    { key: 'protectDriverTempEnable', label: '驱动温度保护启用' },
    { key: 'protectUnderVoltageEnable', label: '欠压保护启用' },
    { key: 'protectOverVoltageEnable', label: '过压保护启用' },
    { key: 'protectOverCurrentEnable', label: '过流保护启用' },
    { key: 'protectShortCircuitEnable', label: '短路保护启用' },
    { key: 'protectStallEnable', label: '失速保护启用' },
    { key: 'protectLostInputEnable', label: '失控保护启用' },
    { key: 'protectMotorTemp', label: '电机温度阈值(℃)' },
    { key: 'protectDriverTemp', label: '驱动温度阈值(℃)' },
    { key: 'protectUnderVoltage', label: '欠压阈值(V)' },
    { key: 'protectOverVoltage', label: '过压阈值(V)' },
    { key: 'protectOverCurrent', label: '过流阈值(mA)' },
    { key: 'protectOverCurrentTime', label: '过流响应时间(ms)' },
    { key: 'protectStallTime', label: '失速响应时间(ms)' },
    { key: 'protectLostInputTime', label: '失控响应时间(ms)' },
    { key: 'brakeResEnable', label: '制动电阻启用' },
    { key: 'brakeResOnVoltage', label: '制动开启电压(V)' },
    { key: 'inputType', label: '输入类型' },
    { key: 'pwmInputControlMode', label: 'PWM 控制模式' },
    { key: 'pwmInputMinValue', label: 'PWM 最小值' },
    { key: 'pwmInputMaxValue', label: 'PWM 最大值' },
    { key: 'pwmInputCenterValue', label: 'PWM 中心值' },
    { key: 'pwmInputDeadband', label: 'PWM 死区' },
    { key: 'pwmToTorqueRatio', label: 'PWM→力矩比' },
    { key: 'pwmToSpeedRatio', label: 'PWM→速度比' },
    { key: 'pwmToAngleRatio', label: 'PWM→角度比' },
    { key: 'pulsesPerCircle', label: '每圈脉冲数' },
    { key: 'anglePidKp', label: '角度 PID Kp' },
    { key: 'anglePidKi', label: '角度 PID Ki' },
    { key: 'anglePidKd', label: '角度 PID Kd' },
    { key: 'speedPidKp', label: '速度 PID Kp' },
    { key: 'speedPidKi', label: '速度 PID Ki' },
    { key: 'speedPidKd', label: '速度 PID Kd' },
    { key: 'currentPidKp', label: '电流 PID Kp' },
    { key: 'currentPidKi', label: '电流 PID Ki' },
    { key: 'currentPidKd', label: '电流 PID Kd' },
    { key: 'maxTorque', label: '最大力矩/功率' }, // MS 型由能力 API 覆盖为"最大功率"
    { key: 'maxSpeed', label: '最大速度(dps)' },
    { key: 'maxAngle', label: '最大角度(°)' },
    { key: 'currentRamp', label: '电流斜率' },
    { key: 'speedRamp', label: '速度斜率(dps/s)' },
    { key: 'uniqueId', label: '唯一 ID' },
    { key: 'savedFlag', label: '保存标志' },
]

const calibFields: { key: EditableCalibKey; label: string }[] = [
    { key: 'motorPoles', label: '电机极对数' },
    { key: 'encoderType', label: '编码器类型' },
    { key: 'encoderPos', label: '编码器位置' },
    { key: 'motorPhaseSequence', label: '电机相序' },
    { key: 'motorEncoderAlignBias', label: '电机编码器对齐偏置' },
    { key: 'motorEncoderAlignRatio', label: '电机编码器对齐比率' },
    { key: 'motorEncoderAlignVoltage', label: '对齐电压(V)' },
    { key: 'motorEncoderAlignFlag', label: '对齐标志' },
    { key: 'encoderOffset', label: '编码器零点偏置' },
    { key: 'encoderOffsetFlag', label: '编码器偏置标志' },
]

// MG/MG_E 专有字段（动态显示）
type MgEditableKey = Exclude<keyof NonNullable<KtechCalibDto['mg']>, 'alignValueList'>
const mgFields: { key: MgEditableKey; label: string }[] = [
    { key: 'reductionRatio', label: '减速比(MG_E)' },
    { key: 'encoderRelateValue', label: '编码器关联值' },
    { key: 'encoderRelateValueFlag', label: '编码器关联标志' },
    { key: 'encoder2Offset', label: '第二编码器偏置(MG_E)' },
    { key: 'encoder2OffsetFlag', label: '第二编码器偏置标志' },
]

// 模板直接使用 calibFields 和 settingFields，无需过滤。

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
            <Button variant="outline" size="sm" @click="goBack">
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
        <Card>
            <CardContent class="p-3">
                <!-- 采样开关勾选框 -->
                <div class="mb-2 flex items-center gap-2">
                    <input
                        id="sampling-toggle"
                        type="checkbox"
                        class="h-4 w-4 cursor-pointer accent-primary"
                        :checked="isSamplingEnabled"
                        @change="toggleSampling"
                    />
                    <label for="sampling-toggle" class="cursor-pointer select-none text-xs">
                        {{ t('ktechConsole.realtimeSampling') }}
                    </label>
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
            </CardContent>
        </Card>

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
        <Card v-if="activeTab === 'info'">
            <CardContent class="p-4 space-y-4">
                <div class="flex flex-wrap gap-2">
                    <Button size="sm" :disabled="acting" @click="handleLoadProductInfo">
                        <RefreshCw class="size-4" />
                        {{ t('ktechConsole.btnLoadProductInfo') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleLoadDeviceType">
                        {{ t('ktechConsole.btnDeviceType') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleConnect">
                        <Power class="size-4" />
                        {{ t('ktechConsole.btnConnect') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleDisconnect">
                        <PowerOff class="size-4" />
                        {{ t('ktechConsole.btnDisconnect') }}
                    </Button>
                    <Button
                        size="sm"
                        variant="outline"
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
            </CardContent>
        </Card>

        <!-- ──────── 参数 Tab（标定 + 设置 合并） ──────── -->
        <Card v-if="activeTab === 'params'">
            <CardContent class="p-4 space-y-6">
                <!-- 顶部统一按钮区 -->
                <div class="flex flex-wrap gap-2">
                    <Button size="sm" :disabled="acting" @click="handleLoadParams">
                        <RefreshCw class="size-4" />
                        {{ t('ktechConsole.btnLoadParams') }}
                    </Button>
                    <Button
                        size="sm"
                        class="bg-blue-600 text-white hover:bg-blue-700"
                        :disabled="acting"
                        @click="handleSaveParams"
                    >
                        {{ t('ktechConsole.btnSaveParams') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleAlignMotorEncoder">
                        {{ t('ktechConsole.btnAlignEncoder') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleSetEncoderZero">
                        {{ t('ktechConsole.btnSetEncoderZero') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleResetCalib">
                        {{ t('ktechConsole.btnResetCalib') }}
                    </Button>
                    <Button
                        size="sm"
                        variant="outline"
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
                            <input
                                v-if="hasFieldOptions(f.key) && isReadOnlyCalib(f.key)"
                                :value="fieldOptionLabel(f.key, calibForm[f.key] as number)"
                                class="rounded border bg-muted px-2 py-1.5 font-mono text-sm opacity-70 cursor-not-allowed"
                                readonly
                            />
                            <!-- 有枚举 + 可写：真下拉 -->
                            <select
                                v-else-if="hasFieldOptions(f.key)"
                                v-model.number="(calibForm as any)[f.key]"
                                class="rounded border bg-background px-2 py-1.5 text-sm"
                            >
                                <option v-for="o in fieldOptions(f.key)" :key="o.value" :value="o.value">
                                    {{ o.label }}
                                </option>
                            </select>
                            <!-- 数字字段 -->
                            <input
                                v-else
                                v-model.number="calibForm[f.key]"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm disabled:opacity-60 disabled:cursor-not-allowed"
                                type="number"
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
                                    <input
                                        v-model.number="calibForm.mg[f.key]"
                                        class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                        type="number"
                                    />
                                </label>
                            </div>
                            <div class="space-y-2">
                                <div class="text-xs text-muted-foreground">{{ t('ktechConsole.alignValueList') }}</div>
                                <div class="grid grid-cols-4 md:grid-cols-8 gap-1.5">
                                    <input
                                        v-for="(_, i) in calibForm.mg.alignValueList"
                                        :key="i"
                                        v-model.number="calibForm.mg.alignValueList[i]"
                                        class="rounded border bg-background px-1.5 py-1 font-mono text-xs"
                                        type="number"
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
                            <input
                                v-if="hasFieldOptions(f.key) && isReadOnlySetting(f.key)"
                                :value="fieldOptionLabel(f.key, settingForm[f.key] as number)"
                                class="rounded border bg-muted px-2 py-1.5 font-mono text-sm opacity-70 cursor-not-allowed"
                                readonly
                            />
                            <!-- 有枚举 + 可写：真下拉；protect enable 值为 0（不支持）时禁用 -->
                            <select
                                v-else-if="hasFieldOptions(f.key)"
                                v-model.number="(settingForm as any)[f.key]"
                                class="rounded border bg-background px-2 py-1.5 text-sm disabled:opacity-60 disabled:cursor-not-allowed"
                                :disabled="isDisabledWhenZero(f.key) && (settingForm as any)[f.key] === 0"
                            >
                                <option
                                    v-for="o in selectableOptions(f.key, (settingForm as any)[f.key])"
                                    :key="o.value"
                                    :value="o.value"
                                >
                                    {{ o.label }}
                                </option>
                            </select>
                            <!-- 数字字段 -->
                            <input
                                v-else
                                v-model.number="settingForm[f.key]"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm disabled:opacity-60 disabled:cursor-not-allowed"
                                type="number"
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
                            <input
                                v-model.number="pidRamForm.angleKp"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidAngleKi') }}</span>
                            <input
                                v-model.number="pidRamForm.angleKi"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidAngleKd') }}</span>
                            <input
                                v-model.number="pidRamForm.angleKd"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidSpeedKp') }}</span>
                            <input
                                v-model.number="pidRamForm.speedKp"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidSpeedKi') }}</span>
                            <input
                                v-model.number="pidRamForm.speedKi"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidSpeedKd') }}</span>
                            <input
                                v-model.number="pidRamForm.speedKd"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidCurrentKp') }}</span>
                            <input
                                v-model.number="pidRamForm.currentKp"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidCurrentKi') }}</span>
                            <input
                                v-model.number="pidRamForm.currentKi"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">{{ t('ktechConsole.pidCurrentKd') }}</span>
                            <input
                                v-model.number="pidRamForm.currentKd"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                type="number"
                            />
                        </label>
                    </div>
                    <Button class="mt-3" size="sm" :disabled="acting" @click="handleWritePidRam">
                        {{ t('ktechConsole.btnWritePidRam') }}
                    </Button>
                </section>
            </CardContent>
        </Card>

        <!-- ──────── 运动控制 Tab ──────── -->
        <Card v-if="activeTab === 'motion'">
            <CardContent class="p-4 space-y-4">
                <!-- 基础动作 -->
                <div class="flex flex-wrap gap-2">
                    <Button
                        size="sm"
                        class="bg-green-600 text-white hover:bg-green-700"
                        :disabled="acting"
                        @click="handleMotorOn"
                    >
                        <Power class="size-4" />
                        {{ t('ktechConsole.btnMotorOn') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleMotorOff">
                        <PowerOff class="size-4" />
                        {{ t('ktechConsole.btnMotorOff') }}
                    </Button>
                    <Button
                        size="sm"
                        variant="outline"
                        class="text-red-600"
                        :disabled="acting"
                        @click="handleMotorStop"
                    >
                        <CircleStop class="size-4" />
                        {{ t('ktechConsole.btnMotorStop') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleMotorRestore">
                        {{ t('ktechConsole.btnMotorRestore') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleBrakeRelease">
                        {{ t('ktechConsole.btnBrakeRelease') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleBrakeApply">
                        {{ t('ktechConsole.btnBrakeApply') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleClearLoops">
                        {{ t('ktechConsole.btnClearLoops') }}
                    </Button>
                    <Button size="sm" variant="outline" :disabled="acting" @click="handleSetMotorZeroRam">
                        {{ t('ktechConsole.btnSetZeroRam') }}
                    </Button>
                    <Button
                        size="sm"
                        variant="outline"
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
                        <select
                            v-model.number="motionForm.mode"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                        >
                            <option v-for="m in filteredMotionModes" :key="m" :value="m">{{ modeLabel(m) }}</option>
                        </select>
                    </div>

                    <div class="grid grid-cols-2 md:grid-cols-4 gap-3">
                        <label v-if="motionForm.mode === KtechMotionMode.Open" class="flex flex-col gap-1 text-xs">
                            <span class="text-muted-foreground">
                                {{ fieldLabel('voltage', t('ktechConsole.fieldVoltage')) }}
                                <span v-if="fieldRange('voltage')?.unit" class="text-[10px] opacity-70">
                                    ({{ fieldRange('voltage')?.unit }})
                                </span>
                            </span>
                            <input
                                v-model.number="motionForm.voltage"
                                type="number"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
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
                            <input
                                v-model.number="motionForm.torqueOrPower"
                                type="number"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
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
                            <input
                                v-model.number="motionForm.speedSignedDps"
                                type="number"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
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
                            <input
                                v-model.number="motionForm.angleDeg"
                                type="number"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
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
                            <input
                                v-model.number="motionForm.singleAngleDeg"
                                type="number"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
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
                            <select
                                v-model.number="motionForm.direction"
                                class="rounded border bg-background px-2 py-1.5 text-sm"
                            >
                                <option :value="0">0 - 正向 (Forward)</option>
                                <option :value="1">1 - 反向 (Rev)</option>
                            </select>
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
                            <input
                                v-model.number="motionForm.speedDps"
                                type="number"
                                class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                :min="fieldRange('speedDps')?.min ?? undefined"
                                :max="fieldRange('speedDps')?.max ?? undefined"
                                :step="fieldRange('speedDps')?.step ?? undefined"
                            />
                        </label>
                    </div>

                    <Button
                        size="sm"
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
                        <input
                            type="file"
                            accept=".bin,.hex,.kf"
                            @change="handleFileChange"
                            class="text-sm"
                            :disabled="uploading"
                        />
                        <Button
                            size="sm"
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
            </CardContent>
        </Card>

        <!-- ──────── 日志 Tab ──────── -->
        <Card v-if="activeTab === 'log'">
            <CardContent class="p-4 space-y-2">
                <div class="flex items-center gap-2">
                    <h3 class="text-sm font-medium">{{ t('ktechConsole.logTitle', { count: store.logs.length }) }}</h3>
                    <Button size="xs" variant="outline" @click="store.logs.splice(0)">
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
            </CardContent>
        </Card>
    </div>
</template>
