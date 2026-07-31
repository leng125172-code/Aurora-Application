import { httpClient } from '@/api/client'

export enum PlcProtocolType {
    OpcUa,
    SiemensS7,
    OmronFinsTcp,
    OmronFinsUdp,
    KeyenceMc,
}
export enum PlcConnectionStatus {
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Faulted,
}
export enum PlcAuthenticationType {
    Anonymous,
    UserName,
    Certificate,
}
export enum PlcMessageSecurityMode {
    None,
    Sign,
    SignAndEncrypt,
}
export enum PlcTagDataType {
    Boolean,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
    String,
    DateTime,
    ByteString,
}
export enum PlcTagAccess {
    None,
    Read,
    Write,
    ReadWrite,
}

export interface PlcDriverDescriptor {
    driverId: string
    protocol: PlcProtocolType
    displayName: string
    capabilities: number
    isInstalled: boolean
}
export interface PlcDeviceDto {
    id: string
    name: string
    protocol: PlcProtocolType
    driverId: string
    driverInstalled: boolean
    capabilities: number
    isEnabled: boolean
    endpointUrl: string
    authenticationType: PlcAuthenticationType
    userName?: string
    hasPassword: boolean
    clientCertificatePath?: string
    hasPrivateKey: boolean
    securityPolicy: string
    messageSecurityMode: PlcMessageSecurityMode
    autoTrustServerCertificate: boolean
    connectTimeoutMs: number
    operationTimeoutMs: number
    sessionTimeoutMs: number
    keepAliveMs: number
    reconnectInitialMs: number
    reconnectMaxMs: number
    idleTimeoutMs: number
    connectionStatus: PlcConnectionStatus
    lastConnectedAt?: string
    lastError?: string
}
export interface SavePlcDeviceDto extends Omit<PlcDeviceDto, 'id' | 'driverInstalled' | 'capabilities' | 'hasPassword' | 'hasPrivateKey' | 'connectionStatus' | 'lastConnectedAt' | 'lastError'> {
    password?: string
    clientCertificatePassword?: string
}
export interface PlcTagDto {
    id: string
    plcDeviceId: string
    code: string
    name: string
    address: string
    dataType: PlcTagDataType
    access: PlcTagAccess
    isEnabled: boolean
    samplingIntervalMs: number
    deadband?: number
    scale: number
    offset: number
    unit?: string
    displayFormat?: string
    minimum?: number
    maximum?: number
}
export type SavePlcTagDto = Omit<PlcTagDto, 'id' | 'plcDeviceId'>
export interface PlcTagValueDto {
    tagId: string
    code: string
    rawValue: unknown
    engineeringValue: unknown
    dataType: PlcTagDataType
    quality: string
    sourceTimestamp?: string
    serverTimestamp?: string
    receivedAt: string
    error?: string
}
export interface PlcBrowseNode {
    address: string
    browseName: string
    displayName: string
    nodeClass: string
    dataType?: PlcTagDataType
    access: PlcTagAccess
    hasChildren: boolean
}

const BASE = '/api/app/plc-device'
const items = <T>(data: { items: T[] } | T[]): T[] => (Array.isArray(data) ? data : data.items)

export async function getPlcDrivers(): Promise<PlcDriverDescriptor[]> {
    const { data } = await httpClient.get<{ items: PlcDriverDescriptor[] }>(`${BASE}/drivers`)
    return items(data)
}
export async function getPlcs(): Promise<PlcDeviceDto[]> {
    const { data } = await httpClient.get<{ items: PlcDeviceDto[] }>(BASE)
    return items(data)
}
export async function getPlc(id: string): Promise<PlcDeviceDto> {
    return (await httpClient.get<PlcDeviceDto>(`${BASE}/${id}`)).data
}
export async function createPlc(input: SavePlcDeviceDto): Promise<PlcDeviceDto> {
    return (await httpClient.post<PlcDeviceDto>(BASE, input)).data
}
export async function updatePlc(id: string, input: SavePlcDeviceDto): Promise<PlcDeviceDto> {
    return (await httpClient.put<PlcDeviceDto>(`${BASE}/${id}`, input)).data
}
export async function deletePlc(id: string): Promise<void> {
    await httpClient.delete(`${BASE}/${id}`)
}
export async function testPlc(id: string): Promise<{ success: boolean; error?: string; durationMs: number }> {
    return (await httpClient.post(`${BASE}/${id}/test-connection`)).data
}
export async function connectPlc(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/connect`)
}
export async function disconnectPlc(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/disconnect`)
}
export async function confirmPlcCertificate(id: string, thumbprint: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/confirm-server-certificate`, { thumbprint })
}
export async function browsePlc(id: string, parentAddress?: string): Promise<{ items: PlcBrowseNode[]; continuationToken?: string }> {
    return (await httpClient.post(`${BASE}/${id}/browse`, { parentAddress, maxResults: 500 })).data
}
export async function getPlcTags(id: string): Promise<PlcTagDto[]> {
    const { data } = await httpClient.get<{ items: PlcTagDto[] }>(`${BASE}/${id}/tags`)
    return items(data)
}
export async function createPlcTag(id: string, input: SavePlcTagDto): Promise<PlcTagDto> {
    return (await httpClient.post<PlcTagDto>(`${BASE}/${id}/tag`, input)).data
}
export async function updatePlcTag(id: string, tagId: string, input: SavePlcTagDto): Promise<PlcTagDto> {
    return (await httpClient.put<PlcTagDto>(`${BASE}/${id}/tag/${tagId}`, input)).data
}
export async function deletePlcTag(id: string, tagId: string): Promise<void> {
    await httpClient.delete(`${BASE}/${id}/tag/${tagId}`)
}
export async function readPlcTags(id: string, tagIds: string[]): Promise<PlcTagValueDto[]> {
    const { data } = await httpClient.post<{ items: PlcTagValueDto[] }>(`${BASE}/${id}/read`, { tagIds })
    return items(data)
}
export async function writePlcTag(id: string, tagId: string, value: unknown): Promise<void> {
    await httpClient.post(`${BASE}/${id}/write`, { items: [{ tagId, value }] })
}
export async function subscribePlcTags(id: string, connectionId: string, tagIds: string[]): Promise<string> {
    const { data } = await httpClient.post<{ subscriptionId: string }>(`${BASE}/${id}/subscribe`, { connectionId, tagIds })
    return data.subscriptionId
}
export async function unsubscribePlcTags(id: string, subscriptionId: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/unsubscribe`, undefined, { params: { subscriptionId } })
}
