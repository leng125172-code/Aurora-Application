import { httpClient } from '@/api/client'

// ─── 类型定义 ──────────────────────────────────────────────────────────────

/** 雷赛 iCL-RS 实时状态快照（与后端 LeisaiStateSnapshotDto 对齐）。 */
export interface LeisaiStateSnapshotDto {
    readonly axisId: string
    readonly slaveId: number
    readonly timestampMs: number
    readonly elapsedMs: number
    readonly isSuccess: boolean
    readonly failureReason: string | null

    readonly statusWord: number
    readonly isFault: boolean
    readonly isEnabled: boolean
    readonly isRunning: boolean
    readonly isCommandDone: boolean
    readonly isPathDone: boolean
    readonly isHomeDone: boolean

    readonly triggerWord: number
    readonly triggerMode: string

    readonly commandPosition: number
    readonly actualPosition: number

    readonly effectiveSpeed: number
    readonly speedSource: string

    readonly busVoltageVolt: number

    readonly inputIoBitmap: number
    readonly outputIoBitmap: number

    readonly currentFaultCode: number
    readonly prWarningCode: number
}

/** DI/DO 功能配置。 */
export interface LeisaiIoConfigDto {
    readonly axisId: string
    readonly diFunctionCodes: number[]
    readonly doFunctionCodes: number[]
}

/** DO 输出请求。 */
export interface LeisaiWriteOutputInputDto {
    doIndex: number
    value: boolean
}

/** 清除故障请求（mode: "current" / "all"）。 */
export interface LeisaiClearFaultInputDto {
    mode: 'current' | 'all'
}

/** 手动 Modbus 读请求（FC03）。 */
export interface LeisaiRawModbusReadInputDto {
    functionCode: number
    startAddress: number
    quantity: number
}

/** 手动 Modbus 写请求（FC06 / FC16）。 */
export interface LeisaiRawModbusWriteInputDto {
    functionCode: number
    startAddress: number
    values: number[]
}

/** 手动 Modbus 命令结果。 */
export interface LeisaiRawModbusResultDto {
    readonly success: boolean
    readonly rawRequest: string
    readonly rawResponse: string
    readonly parsedValues: number[]
    readonly errorMessage: string | null
    readonly elapsedMs: number
}

/** 采样开关状态。 */
export interface LeisaiSamplingStateDto {
    readonly isPollingEnabled: boolean
}

/** 雷赛 iCL-RS 位置-速度滚动轨迹（与后端 LeisaiTraceDto 对齐，最多 1200 点）。 */
export interface LeisaiTraceDto {
    readonly axisId: string
    /** 实际位置序列（脉冲），从旧到新，与 commandPositions / speeds 等长。 */
    readonly actualPositions: number[]
    /** 指令位置序列（脉冲），从旧到新。 */
    readonly commandPositions: number[]
    /** 有效速度序列（rpm），从旧到新。 */
    readonly speeds: number[]
}

// ─── DI/DO 功能码映射（前端常量） ───────────────────────────────────────────
// 雷赛 iCL-RS 用户手册 Pr1.x 配置参数定义，bit7=1 表示常闭。
export const LeisaiDiFunctionLabels: Record<number, string> = {
    0x00: '无功能',
    0x01: '伺服使能',
    0x02: '故障清除',
    0x03: '正向 JOG',
    0x04: '反向 JOG',
    0x05: '强制停止',
    0x06: '正限位',
    0x07: '反限位',
    0x08: '原点信号',
    0x09: '路径触发',
    0x0a: '路径地址 bit0',
    0x0b: '路径地址 bit1',
    0x0c: '路径地址 bit2',
    0x0d: '路径地址 bit3',
}

export const LeisaiDoFunctionLabels: Record<number, string> = {
    0x00: '无功能',
    0x01: '伺服就绪',
    0x02: '电机使能',
    0x03: '抱闸输出',
    0x04: '故障输出',
    0x05: '到位信号',
    0x06: '回零完成',
    0x07: '指令完成',
}

// ─── REST 客户端 ────────────────────────────────────────────────────────────

const BASE = '/api/app/leisai-motor'

export async function getState(id: string): Promise<LeisaiStateSnapshotDto> {
    const { data } = await httpClient.get<LeisaiStateSnapshotDto>(`${BASE}/${id}/state`)
    return data
}

export async function getIoConfig(id: string): Promise<LeisaiIoConfigDto> {
    const { data } = await httpClient.get<LeisaiIoConfigDto>(`${BASE}/${id}/io-config`)
    return data
}

export async function writeOutput(id: string, dto: LeisaiWriteOutputInputDto): Promise<void> {
    await httpClient.post(`${BASE}/${id}/write-output`, dto)
}

export async function clearFault(id: string, dto: LeisaiClearFaultInputDto): Promise<void> {
    await httpClient.post(`${BASE}/${id}/clear-fault`, dto)
}

export async function saveToEeprom(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/save-to-eeprom`)
}

export async function rawRead(id: string, dto: LeisaiRawModbusReadInputDto): Promise<LeisaiRawModbusResultDto> {
    const { data } = await httpClient.post<LeisaiRawModbusResultDto>(`${BASE}/${id}/raw-read`, dto)
    return data
}

export async function rawWrite(id: string, dto: LeisaiRawModbusWriteInputDto): Promise<LeisaiRawModbusResultDto> {
    const { data } = await httpClient.post<LeisaiRawModbusResultDto>(`${BASE}/${id}/raw-write`, dto)
    return data
}

export async function getSamplingState(id: string): Promise<LeisaiSamplingStateDto> {
    const { data } = await httpClient.get<LeisaiSamplingStateDto>(`${BASE}/${id}/sampling-state`)
    return data
}

export async function enableSampling(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/enable-sampling`)
}

export async function disableSampling(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/disable-sampling`)
}

// ─── Phase 2：参数配置 ─────────────────────────────────────────────────────

/** 参数数据类型分类（与后端 LeisaiParamDataKind 对齐）。 */
export type LeisaiParamDataKind = 0 | 1 | 2 // 0=Uint16, 1=Int16, 2=Int32Pair

/** 参数元数据。 */
export interface LeisaiParameterMetadataDto {
    readonly pr: string
    readonly group: number
    readonly addressLow: number
    readonly name: string
    readonly description: string
    readonly unit: string
    readonly rangeMin: number | null
    readonly rangeMax: number | null
    readonly defaultValue: number | null
    readonly dataKind: LeisaiParamDataKind
    /** 带 group 筛选时由后端实时读取并填充；全量查询时为 null。 */
    readonly currentValue: number | null
}

/** 单参数读取结果（FC03 读回的当前寄存器值）。 */
export interface LeisaiParameterReadResultDto {
    readonly addressLow: number
    /** 设备返回的当前值。 */
    readonly currentValue: number
    readonly isSuccess: boolean
    readonly errorMessage: string | null
}

/** 单参数写入结果（FC06 写入的寄存器值）。 */
export interface LeisaiParameterValueDto {
    readonly addressLow: number
    /** 写入到设备的值。 */
    readonly value: number
    readonly isSuccess: boolean
    readonly errorMessage: string | null
}

/** 批量读取结果（FC03）。 */
export interface LeisaiBatchReadResultDto {
    readonly items: LeisaiParameterReadResultDto[]
    readonly elapsedMs: number
    readonly successCount: number
    readonly failureCount: number
}

/** 批量写入结果（FC06）。 */
export interface LeisaiBatchResultDto {
    readonly items: LeisaiParameterValueDto[]
    readonly elapsedMs: number
    readonly successCount: number
    readonly failureCount: number
}

/** 分组中文名（前端兜底；后端如有可覆盖）。 */
export const LeisaiParamGroupNames: Record<number, string> = {
    0: '基础参数 (Pr0)',
    1: '增益与滤波 (Pr1)',
    2: '高级控制 (Pr2)',
    3: '保留 (Pr3)',
    4: 'IO 与制动 (Pr4)',
    5: '电流/通讯 (Pr5)',
    6: 'JOG / 试运行 (Pr6)',
    7: '电机/电流环 (Pr7)',
    8: 'PR 控制与回零 (Pr8)',
    9: 'PR 路径配置 (Pr9)',
}

export async function getParameterMetadata(id: string, group?: number): Promise<LeisaiParameterMetadataDto[]> {
    // ABP 约定：Get + id → GET /{id}/parameter-metadata；group 为可选查询参数
    const { data } = await httpClient.get<LeisaiParameterMetadataDto[]>(`${BASE}/${id}/parameter-metadata`, {
        params: group !== undefined ? { group } : undefined,
    })
    return data
}

export async function batchReadParameters(id: string, addressLows: number[]): Promise<LeisaiBatchReadResultDto> {
    const { data } = await httpClient.post<LeisaiBatchReadResultDto>(`${BASE}/${id}/batch-read-parameters`, {
        addressLows,
    })
    return data
}

export async function batchWriteParameters(
    id: string,
    items: { addressLow: number; value: number }[]
): Promise<LeisaiBatchResultDto> {
    const { data } = await httpClient.post<LeisaiBatchResultDto>(`${BASE}/${id}/batch-write-parameters`, { items })
    return data
}

export async function readGroupParameters(id: string, group: number): Promise<LeisaiBatchReadResultDto> {
    const { data } = await httpClient.post<LeisaiBatchReadResultDto>(`${BASE}/${id}/read-group-parameters`, null, {
        params: { group },
    })
    return data
}

export async function resetParametersToDefault(id: string, addressLows: number[]): Promise<LeisaiBatchResultDto> {
    const { data } = await httpClient.post<LeisaiBatchResultDto>(`${BASE}/${id}/reset-parameters-to-default`, {
        addressLows,
    })
    return data
}
