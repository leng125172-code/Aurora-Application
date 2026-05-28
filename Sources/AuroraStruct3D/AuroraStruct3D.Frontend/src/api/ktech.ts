import { httpClient } from '@/api/client'

// ─── 类型定义 ──────────────────────────────────────────────────────────────

/** 瓴控运动模式（与后端 KtechMotionMode 枚举一一对应）。 */
export enum KtechMotionMode {
    Open = 0,
    TorqueOrPower = 1,
    Speed = 2,
    MultiAngle = 3,
    MultiAngleWithSpeed = 4,
    SingleAngle = 5,
    SingleAngleWithSpeed = 6,
    IncrementAngle = 7,
    IncrementAngleWithSpeed = 8,
}

/** 升级阶段枚举（与后端 KtechUpgradeStage 枚举对齐）。 */
export enum KtechUpgradeStage {
    Started = 0,
    WaitingHandshake = 1,
    HeaderSent = 2,
    Transferring = 3,
    Finalizing = 4,
    Completed = 5,
    Failed = 99,
}

export interface KtechProductInfoDto {
    readonly deviceTypeCode: number
    readonly deviceTypeName: string
    readonly driverName: string
    readonly motorName: string
    readonly chipId: string
    readonly hardwareVersion: string
    readonly motorVersion: string
    readonly firmwareVersion: string
}

/** 字段量程描述（与后端 KtechFieldRange 对齐）。 */
export interface KtechFieldRange {
    readonly min: number | null
    readonly max: number | null
    readonly step: number | null
    readonly unit: string | null
}

/** 设备能力描述（与后端 KtechCapabilitiesDto 对齐）。 */
export interface KtechCapabilitiesDto {
    readonly deviceTypeCode: number
    readonly modelName: string
    readonly supportedControlModes: KtechMotionMode[]
    readonly readOnlySettingFields: string[]
    readonly readOnlyCalibFields: string[]
    readonly fieldRanges: Record<string, KtechFieldRange>
    readonly fieldLabelOverrides: Record<string, string>
}

export interface KtechCalibMgExtraDto {
    /** 对齐校准值数组（32 个 u16） */
    alignValueList: number[]
    /** 减速比（仅 MG_E 有效） */
    reductionRatio: number
    /** 编码器关联值（u32） */
    encoderRelateValue: number
    /** 编码器关联值标志 */
    encoderRelateValueFlag: number
    /** 第二编码器偏置（u32，MG_E 专用） */
    encoder2Offset: number
    /** 第二编码器偏置标志 */
    encoder2OffsetFlag: number
}

/**
 * 瓴控 KTECH 标定参数 DTO（CMD 0x16/0x17）。
 * 通用字段在所有型号有效；DeviceTypeCode=8241(MG)/8242(MG_E) 时 `mg` 子块被填充。
 */
export interface KtechCalibDto {
    /** 设备类型码：0=未知, 8209=MS, 8225=MF, 8241=MG, 8242=MG_E, 8257=MH */
    deviceTypeCode: number
    motorPoles: number
    encoderType: number
    encoderPos: number
    motorPhaseSequence: number
    motorEncoderAlignBias: number
    motorEncoderAlignRatio: number
    motorEncoderAlignVoltage: number
    motorEncoderAlignFlag: number
    encoderOffset: number
    encoderOffsetFlag: number
    /** 保存标志（一般为 0x55555555） */
    savedFlag: number
    /** MG/MG_E 专有字段；其他型号为 null */
    mg: KtechCalibMgExtraDto | null
}

/**
 * 瓴控 KTECH 设置参数 DTO（CMD 0x14/0x15），字段顺序/类型与 Demo saveSetting_t 对齐。
 */
export interface KtechSettingDto {
    driverId: number
    busType: number
    rs485BaudRate: number
    canBaudRate: number
    broadcastMode: number
    spinDirection: number
    // 保护开关
    protectMotorTempEnable: number
    protectDriverTempEnable: number
    protectUnderVoltageEnable: number
    protectOverVoltageEnable: number
    protectOverCurrentEnable: number
    protectShortCircuitEnable: number
    protectStallEnable: number
    protectLostInputEnable: number
    // 保护阈值
    protectMotorTemp: number
    protectDriverTemp: number
    protectUnderVoltage: number
    protectOverVoltage: number
    protectOverCurrent: number
    protectOverCurrentTime: number
    protectStallTime: number
    protectLostInputTime: number
    // 制动
    brakeResEnable: number
    brakeResOnVoltage: number
    // 输入 / PWM
    inputType: number
    pwmInputControlMode: number
    pwmInputMinValue: number
    pwmInputMaxValue: number
    pwmInputCenterValue: number
    pwmInputDeadband: number
    pwmToTorqueRatio: number
    pwmToSpeedRatio: number
    pwmToAngleRatio: number
    pulsesPerCircle: number
    // PID（ushort）
    anglePidKp: number
    anglePidKi: number
    anglePidKd: number
    speedPidKp: number
    speedPidKi: number
    speedPidKd: number
    currentPidKp: number
    currentPidKi: number
    currentPidKd: number
    // 限制
    maxTorque: number
    maxSpeed: number
    /** s64 — JS 用 number 表达，注意 ±2^53 范围 */
    maxAngle: number
    currentRamp: number
    speedRamp: number
    // 标识
    uniqueId: number
    savedFlag: number
}

export interface KtechErrorFlagsDto {
    readonly underVoltage: boolean
    readonly overVoltage: boolean
    readonly driverOverTemp: boolean
    readonly motorOverTemp: boolean
    readonly overCurrent: boolean
    readonly shortCircuit: boolean
    readonly stall: boolean
    readonly lostInput: boolean
    readonly raw: number
}

export interface KtechStateSnapshotDto {
    readonly axisId: string
    readonly slaveId: number
    readonly timestampMs: number
    readonly elapsedMs: number
    readonly isSuccess: boolean
    readonly failureReason: string | null
    readonly motorTemperature: number
    readonly busVoltage: number
    readonly busCurrent: number
    readonly errorFlags: KtechErrorFlagsDto
    readonly torqueOrPower: number
    readonly speed: number
    readonly encoderValue: number
    readonly multiTurnAngle: number
    /** 单圈角度（0.01°/LSB，范围 0..35999） */
    readonly singleTurnAngleCentideg: number
}

export interface KtechState3Dto {
    readonly phaseACurrent: number
    readonly phaseBCurrent: number
    readonly phaseCCurrent: number
}

export interface KtechMotionResponseDto {
    readonly motorTemperature: number
    readonly torqueOrPower: number
    readonly speed: number
    readonly encoderValue: number
}

export interface KtechMotionInputDto {
    mode: KtechMotionMode
    voltage?: number
    torqueOrPower?: number
    speedDps?: number
    speedSignedDps?: number
    angleDeg?: number
    singleAngleDeg?: number
    direction?: number
}

export interface KtechWritePidRamInputDto {
    angleKp: number
    angleKi: number
    angleKd: number
    speedKp: number
    speedKi: number
    speedKd: number
    currentKp: number
    currentKi: number
    currentKd: number
}

export interface KtechUpgradeProgressDto {
    readonly axisId: string
    readonly stage: KtechUpgradeStage
    readonly bytesSent: number
    readonly totalBytes: number
    readonly percent: number
    readonly message: string | null
    readonly errorStep: number | null
}

// ─── API ───────────────────────────────────────────────────────────────────
//
// ABP 约定式 API：当方法包含 `id` 参数时，其会被自动放入 URL 路径，
// 路由模板形如 /api/app/ktech-motor/{id}/{action}（与 motor-device 一致）。
// 方法名前缀决定 HTTP 动词：Get*→GET，Save*→PUT，其他→POST。

const BASE = '/api/app/ktech-motor'

// === Info ===
export async function getDeviceType(id: string): Promise<number> {
    const { data } = await httpClient.get<number>(`${BASE}/${id}/device-type`)
    return data
}

export async function getProductInfo(id: string): Promise<KtechProductInfoDto> {
    const { data } = await httpClient.get<KtechProductInfoDto>(`${BASE}/${id}/product-info`)
    return data
}

/**
 * 获取设备能力描述（前端用于禁用控件、过滤运动模式、限定输入范围）。
 * 后端基于 MotorAxis.KtechDeviceTypeCode 静态推导，不触发硬件读取。
 */
export async function getCapabilities(id: string): Promise<KtechCapabilitiesDto> {
    const { data } = await httpClient.get<KtechCapabilitiesDto>(`${BASE}/${id}/capabilities`)
    return data
}

export async function connectKtech(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/connect`)
}

export async function disconnectKtech(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/disconnect`)
}

export async function rebootKtech(id: string): Promise<void> {
    // RebootDeviceAsync → /reboot-device
    await httpClient.post(`${BASE}/${id}/reboot-device`)
}

// === Calib ===
export async function getCalib(id: string): Promise<KtechCalibDto> {
    const { data } = await httpClient.get<KtechCalibDto>(`${BASE}/${id}/calib`)
    return data
}

export async function saveCalib(id: string, dto: KtechCalibDto): Promise<void> {
    await httpClient.put(`${BASE}/${id}/calib`, dto)
}

export async function alignMotorEncoder(id: string): Promise<KtechCalibDto> {
    const { data } = await httpClient.post<KtechCalibDto>(`${BASE}/${id}/align-motor-encoder`)
    return data
}

export async function setEncoderZero(id: string): Promise<KtechCalibDto> {
    const { data } = await httpClient.post<KtechCalibDto>(`${BASE}/${id}/set-encoder-zero`)
    return data
}

export async function resetCalib(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/reset-calib`)
}

// === Setting ===
export async function getSetting(id: string): Promise<KtechSettingDto> {
    const { data } = await httpClient.get<KtechSettingDto>(`${BASE}/${id}/setting`)
    return data
}

export async function saveSetting(id: string, dto: KtechSettingDto): Promise<void> {
    await httpClient.put(`${BASE}/${id}/setting`, dto)
}

export async function persistSetting(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/persist-setting`)
}

export async function resetSetting(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/reset-setting`)
}

export async function writePidRam(id: string, dto: KtechWritePidRamInputDto): Promise<void> {
    await httpClient.post(`${BASE}/${id}/write-pid-ram`, dto)
}

// === Basic motor ===
export async function motorOn(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/motor-on`)
}

export async function motorOff(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/motor-off`)
}

export async function motorStop(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/motor-stop`)
}

export async function motorRestore(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/motor-restore`)
}

export async function brake(id: string, release: boolean): Promise<void> {
    await httpClient.post(`${BASE}/${id}/brake`, null, { params: { release } })
}

export async function clearLoops(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/clear-loops`)
}

export async function setMotorZeroRam(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/set-motor-zero-ram`)
}

export async function clearError(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/clear-error`)
}

// === Motion ===
export async function motion(id: string, dto: KtechMotionInputDto): Promise<KtechMotionResponseDto> {
    const { data } = await httpClient.post<KtechMotionResponseDto>(`${BASE}/${id}/motion`, dto)
    return data
}

// === State ===
export async function readStateOnce(id: string): Promise<KtechStateSnapshotDto> {
    // ReadStateOnceAsync → GET /state-once
    const { data } = await httpClient.get<KtechStateSnapshotDto>(`${BASE}/${id}/state-once`)
    return data
}

export async function readState3(id: string): Promise<KtechState3Dto> {
    const { data } = await httpClient.get<KtechState3Dto>(`${BASE}/${id}/state3`)
    return data
}

export async function readMultiAngle(id: string): Promise<number> {
    const { data } = await httpClient.get<number>(`${BASE}/${id}/multi-angle`)
    return data
}

export async function readSingleAngle(id: string): Promise<number> {
    const { data } = await httpClient.get<number>(`${BASE}/${id}/single-angle`)
    return data
}

// === Sampling Control ===
/** 获取指定轴实时采样是否启用 */
export async function getSamplingEnabled(id: string): Promise<boolean> {
    const { data } = await httpClient.get<boolean>(`${BASE}/${id}/sampling-enabled`)
    return data
}

/** 启用指定轴的实时采样 */
export async function enableSampling(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/enable-sampling`)
}

/** 暂停指定轴的实时采样 */
export async function disableSampling(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/disable-sampling`)
}

// === Firmware ===
export async function uploadFirmware(id: string, file: File): Promise<void> {
    const form = new FormData()
    form.append('file', file, file.name)
    await httpClient.post(`${BASE}/${id}/upload-firmware`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 600_000,
    })
}
