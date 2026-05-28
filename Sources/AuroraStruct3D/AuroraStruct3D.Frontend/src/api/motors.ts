import { httpClient } from '@/api/client'
import type { PagedResultDto } from '@/api/device-state'

export enum MotorBrand {
    KtechKtech = 0,
    LeisaiIclRs = 1,
}

export enum MotorDeviceStatus {
    Unknown = 0,
    Offline = 1,
    Online = 2,
    Enabled = 3,
    Moving = 4,
    Faulted = 5,
}

export enum MotorRotationAngleLimitKind {
    Minimum = 0,
    Maximum = 1,
}

export interface MotorAxisDto {
    readonly id: string
    readonly name: string
    readonly axisIndex: number
    readonly description: string | null
    readonly isEnabled: boolean
    readonly serialPortConfigId: string
    readonly serialPortDisplayName: string
    readonly portName: string
    readonly baudRate: number
    readonly isSerialPortOpen: boolean
    readonly slaveId: number
    readonly brand: MotorBrand
    readonly brandText: string
    readonly model: string | null
    readonly status: MotorDeviceStatus
    readonly statusText: string
    readonly isHomed: boolean
    readonly lastStatusUpdateAt: string | null
    readonly minRotationAngle: number | null
    readonly maxRotationAngle: number | null
}

export interface SetMotorRotationAngleRangeDto {
    minRotationAngle?: number | null
    maxRotationAngle?: number | null
}

export interface SetMotorRotationAngleLimitFromCurrentDto {
    limitKind: MotorRotationAngleLimitKind
}

export interface GetMotorAxisListDto {
    filter?: string
    isEnabled?: boolean
    refreshHardware?: boolean
    skipCount?: number
    maxResultCount?: number
}

export interface UpdateMotorAxisDto {
    name: string
    description?: string | null
    isEnabled: boolean
    model?: string | null
}

export interface ScanMotorDevicesInput {
    startSlaveId: number
    endSlaveId: number
    baudRates: number[]
    probeTimeoutMs: number
}

export interface DiscoveredMotorDeviceDto {
    readonly serialPortConfigId: string
    readonly portName: string
    readonly baudRate: number
    readonly slaveId: number
    readonly brand: MotorBrand
    readonly brandText: string
    readonly motorAxisId: string
}

export interface ScanMotorDevicesResultDto {
    readonly triedCount: number
    readonly foundCount: number
    readonly elapsedMs: number
    readonly items: DiscoveredMotorDeviceDto[]
}

/**
 * 电机扫描进度事件类型（与后端 MotorScanProgressKind 枚举对齐）。
 */
export enum MotorScanProgressKind {
    Started = 0,
    PortStarted = 1,
    Probing = 2,
    DeviceFound = 3,
    PortFinished = 4,
    PortError = 5,
    Completed = 6,
}

/**
 * 后端通过 SignalR 推送的扫描进度 DTO。
 */
export interface MotorScanProgressDto {
    readonly kind: MotorScanProgressKind
    readonly timestamp: string
    readonly message: string
    readonly serialPortConfigId?: string | null
    readonly portName?: string | null
    readonly baudRate?: number | null
    readonly slaveId?: number | null
    readonly brand?: string | null
    readonly found?: boolean | null
    readonly totalPorts: number
    readonly finishedPorts: number
    readonly triedCount: number
    readonly foundCount: number
    readonly elapsedMs?: number | null
}

export interface MoveMotorInput {
    position: number
    speedRpm: number
}

export interface ReadHoldingRegistersInput {
    startAddress: number
    quantity: number
}

export interface WriteSingleRegisterInput {
    address: number
    value: number
}

export interface RegisterReadResultDto {
    readonly requestHex: string
    readonly responseHex: string
    readonly values: number[]
}

const BASE = '/api/app/motor-device'

export async function getMotorAxisList(params: GetMotorAxisListDto = {}): Promise<PagedResultDto<MotorAxisDto>> {
    const { data } = await httpClient.get<PagedResultDto<MotorAxisDto>>(BASE, { params })
    return data
}

export async function getMotorAxis(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.get<MotorAxisDto>(`${BASE}/${id}`)
    return data
}

export async function scanMotorDevices(dto: ScanMotorDevicesInput): Promise<ScanMotorDevicesResultDto> {
    const { data } = await httpClient.post<ScanMotorDevicesResultDto>(`${BASE}/scan-devices`, dto, {
        timeout: 600_000,
    })
    return data
}

export async function updateMotorAxis(id: string, dto: UpdateMotorAxisDto): Promise<MotorAxisDto> {
    const { data } = await httpClient.put<MotorAxisDto>(`${BASE}/${id}`, dto)
    return data
}

export async function setMotorRotationAngleRange(
    id: string,
    dto: SetMotorRotationAngleRangeDto
): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/set-rotation-angle-range`, dto)
    return data
}

export async function setMotorRotationAngleLimitFromCurrent(
    id: string,
    dto: SetMotorRotationAngleLimitFromCurrentDto
): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/set-rotation-angle-limit-from-current`, dto)
    return data
}

export async function refreshMotorStatus(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/refresh-status`)
    return data
}

export async function enableMotor(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/enable`)
    return data
}

export async function disableMotor(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/disable`)
    return data
}

export async function moveMotorAbsolute(id: string, dto: MoveMotorInput): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/move-absolute`, dto)
    return data
}

export async function moveMotorRelative(id: string, dto: MoveMotorInput): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/move-relative`, dto)
    return data
}

export async function stopMotor(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/stop`)
    return data
}

export async function emergencyStopMotor(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/emergency-stop`)
    return data
}

export async function homeMotor(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/home`)
    return data
}

export async function clearMotorFault(id: string): Promise<MotorAxisDto> {
    const { data } = await httpClient.post<MotorAxisDto>(`${BASE}/${id}/clear-fault`)
    return data
}

export async function readMotorHoldingRegisters(
    id: string,
    dto: ReadHoldingRegistersInput
): Promise<RegisterReadResultDto> {
    const { data } = await httpClient.post<RegisterReadResultDto>(`${BASE}/${id}/read-holding-registers`, dto)
    return data
}

export async function writeMotorSingleRegister(id: string, dto: WriteSingleRegisterInput): Promise<boolean> {
    const { data } = await httpClient.post<boolean>(`${BASE}/${id}/write-single-register`, dto)
    return data
}
