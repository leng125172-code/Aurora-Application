import { httpClient } from '@/api/client'
import type { PagedResultDto } from '@/api/device-state'

export enum SerialPortParity {
    None = 0,
    Odd = 1,
    Even = 2,
    Mark = 3,
    Space = 4,
}

export enum SerialPortStopBits {
    None = 0,
    One = 1,
    Two = 2,
    OnePointFive = 3,
}

export enum SerialPortHandshake {
    None = 0,
    XOnXOff = 1,
    RequestToSend = 2,
    RequestToSendXOnXOff = 3,
}

export interface SerialPortConfigDto {
    readonly id: string
    readonly displayName: string
    readonly description: string | null
    readonly isEnabled: boolean
    readonly portName: string
    readonly baudRate: number
    readonly dataBits: number
    readonly parity: SerialPortParity
    readonly stopBits: SerialPortStopBits
    readonly handshake: SerialPortHandshake
    readonly isOpen: boolean
    readonly supportedBaudRates: readonly number[]
}

export interface GetSerialPortListDto {
    filter?: string
    isEnabled?: boolean
    skipCount?: number
    maxResultCount?: number
}

export interface UpdateSerialPortConfigDto {
    displayName: string
    description?: string | null
    isEnabled: boolean
    baudRate: number
    dataBits: number
    parity: SerialPortParity
    stopBits: SerialPortStopBits
    handshake: SerialPortHandshake
}

export interface ConnectSerialPortDto {
    baudRate?: number
}

export interface SerialPortRawSendDto {
    payload: string
    isHex: boolean
    appendNewLine: boolean
    expectedResponseLength: number
    timeoutMs: number
}

export interface SerialPortRawResponseDto {
    readonly sentHex: string
    readonly responseHex: string
    readonly responseText: string
    readonly responseLength: number
}

export interface SerialPortScanResultDto {
    readonly count: number
    readonly items: SerialPortConfigDto[]
}

const BASE = '/api/app/serial-port'

export async function getSerialPortList(
    params: GetSerialPortListDto = {}
): Promise<PagedResultDto<SerialPortConfigDto>> {
    const { data } = await httpClient.get<PagedResultDto<SerialPortConfigDto>>(BASE, { params })
    return data
}

export async function getSerialPort(id: string): Promise<SerialPortConfigDto> {
    const { data } = await httpClient.get<SerialPortConfigDto>(`${BASE}/${id}`)
    return data
}

export async function scanSystemPorts(): Promise<SerialPortScanResultDto> {
    const { data } = await httpClient.post<SerialPortScanResultDto>(`${BASE}/scan-system-ports`)
    return data
}

export async function updateSerialPort(
    id: string,
    dto: UpdateSerialPortConfigDto
): Promise<SerialPortConfigDto> {
    const { data } = await httpClient.put<SerialPortConfigDto>(`${BASE}/${id}`, dto)
    return data
}

export async function connectSerialPort(id: string, dto: ConnectSerialPortDto): Promise<SerialPortConfigDto> {
    const { data } = await httpClient.post<SerialPortConfigDto>(`${BASE}/${id}/connect`, dto)
    return data
}

export async function disconnectSerialPort(id: string): Promise<SerialPortConfigDto> {
    const { data } = await httpClient.post<SerialPortConfigDto>(`${BASE}/${id}/disconnect`)
    return data
}

export async function sendSerialPortRaw(id: string, dto: SerialPortRawSendDto): Promise<SerialPortRawResponseDto> {
    const { data } = await httpClient.post<SerialPortRawResponseDto>(`${BASE}/${id}/send-raw`, dto)
    return data
}

export interface SerialPortOperationLogDto {
    readonly id: string
    readonly serialPortConfigId: string
    readonly operationType: string
    readonly occurredAt: string
    readonly isSuccess: boolean
    readonly parameterSummary: string | null
    readonly errorMessage: string | null
    readonly roundTripMs: number
}

export interface GetSerialPortLogListDto {
    serialPortConfigId: string
    operationType?: string
    isFailedOnly?: boolean
    startTime?: string
    endTime?: string
    skipCount?: number
    maxResultCount?: number
}

export async function getSerialPortLogs(
    params: GetSerialPortLogListDto
): Promise<PagedResultDto<SerialPortOperationLogDto>> {
    const { data } = await httpClient.get<PagedResultDto<SerialPortOperationLogDto>>(`${BASE}/logs`, { params })
    return data
}
