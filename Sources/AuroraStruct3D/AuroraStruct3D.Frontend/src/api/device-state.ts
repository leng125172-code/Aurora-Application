/**
 * 设备状态相关 REST API
 *
 * 对应后端 IDeviceStateAppService，路由：
 *   GET  /api/app/device-state/current-state        获取当前状态快照（无需登录）
 *   GET  /api/app/device-state/current-fault        获取当前活跃故障（无需登录）
 *   POST /api/app/device-state/switch-mode          切换运行模式（需登录）
 *   GET  /api/app/device-state/fault-paged-list     分页查历史故障（需登录）
 *   GET  /api/app/device-state/state-log-paged-list 分页查状态日志（需登录）
 */
import { httpClient } from '@/api/client'

// ─── 枚举（与后端 C# enum 保持一致）────────────────────────────────────────

export enum DeviceStatus {
    Standby = 0,
    Starting = 1,
    Running = 2,
    Paused = 3,
    Stopping = 4,
    Stopped = 5,
    Resetting = 6,
    FaultAcknowledging = 7,
    Fault = 8,
    EmergencyStop = 9,
    Initializing = 10,
}

export enum DeviceRunMode {
    Online = 0,
    Auto = 1,
    Manual = 2,
    Maintenance = 3,
}

export enum DeviceFaultLevel {
    Warning = 0,
    GeneralFault = 1,
    SevereFault = 2,
    SafetyFault = 3,
}

export enum DeviceFaultSource {
    System = 0,
    Camera = 1,
    Plc = 2,
    Workflow = 3,
}

export enum StateChangeTrigger {
    UserManual = 0,
    SystemAuto = 1,
    DeviceFault = 2,
    EmergencyButton = 3,
    RemoteCommand = 4,
    Watchdog = 5,
    SystemInit = 6,
}

// ─── DTO 类型 ────────────────────────────────────────────────────────────────

export interface DeviceStateDto {
    readonly status: DeviceStatus
    readonly runMode: DeviceRunMode
    readonly currentFaultId: string | null
    readonly currentFaultLevel: DeviceFaultLevel | null
    readonly currentFaultCode: string | null
    readonly isInTransition: boolean
    readonly canAcceptProductionCommand: boolean
    readonly canSwitchMode: boolean
}

export interface DeviceFaultDto {
    readonly id: string
    readonly occurredAt: string
    readonly faultLevel: DeviceFaultLevel
    readonly faultCode: string | null
    readonly faultMessage: string | null
    readonly faultReason: string | null
    readonly source: DeviceFaultSource
    readonly deviceId: string | null
    readonly deviceName: string | null
    readonly workflowProjectId: string | null
    readonly workflowProjectName: string | null
    readonly workflowRunId: string | null
    readonly workflowId: string | null
    readonly workflowName: string | null
    readonly workflowNodeId: string | null
    readonly lastOccurredAt: string
    readonly occurrenceCount: number
    readonly isResolved: boolean
    readonly isAutoRecovered: boolean
    readonly resolverId: string | null
    readonly resolverName: string | null
    readonly resolvedAt: string | null
    readonly resolutionDescription: string | null
    readonly durationMs: number | null
    readonly causedModeSwitch: boolean
    readonly switchedToMode: DeviceRunMode | null
    readonly stateLogId: string | null
    readonly remark: string | null
    readonly creationTime: string
}

export interface DeviceStateLogDto {
    readonly id: string
    readonly occurredAt: string
    readonly previousStatus: DeviceStatus | null
    readonly newStatus: DeviceStatus
    readonly isStatusChange: boolean
    readonly isTransitionState: boolean
    readonly previousMode: DeviceRunMode | null
    readonly newMode: DeviceRunMode
    readonly isModeChange: boolean
    readonly trigger: StateChangeTrigger
    readonly faultId: string | null
    readonly operatorId: string | null
    readonly operatorName: string | null
    readonly reason: string | null
    readonly remark: string | null
    readonly durationMs: number | null
    readonly isSuccessful: boolean
    readonly errorMessage: string | null
    readonly creationTime: string
}

export interface PagedResultDto<T> {
    readonly totalCount: number
    readonly items: T[]
}

export interface SwitchModeInput {
    readonly newMode: DeviceRunMode
    readonly reason?: string | null
}

export interface GetFaultPagedInput {
    readonly skipCount?: number
    readonly maxResultCount?: number
    readonly sorting?: string | null
    readonly faultLevel?: DeviceFaultLevel | null
    readonly source?: DeviceFaultSource | null
    readonly isResolved?: boolean | null
    readonly startTime?: string | null
    readonly endTime?: string | null
}

export interface GetStateLogPagedInput {
    readonly skipCount?: number
    readonly maxResultCount?: number
    readonly sorting?: string | null
    readonly trigger?: StateChangeTrigger | null
    readonly status?: DeviceStatus | null
    readonly startTime?: string | null
    readonly endTime?: string | null
    readonly isSuccessful?: boolean | null
}

// ─── API 函数 ────────────────────────────────────────────────────────────────

/** 获取当前设备状态快照（无需登录） */
export async function getCurrentStateAsync(): Promise<DeviceStateDto> {
    const response = await httpClient.get<DeviceStateDto>('/api/app/device-state/current-state')
    return response.data
}

/** 获取当前活跃故障（无需登录，无故障时返回 null） */
export async function getCurrentFaultAsync(): Promise<DeviceFaultDto | null> {
    const response = await httpClient.get<DeviceFaultDto | null>('/api/app/device-state/current-fault')
    return response.data
}

/** 切换运行模式（需要登录） */
export async function switchModeAsync(input: SwitchModeInput): Promise<void> {
    await httpClient.post('/api/app/device-state/switch-mode', input)
}

/** 分页查询历史故障记录（需要登录） */
export async function getFaultPagedListAsync(input: GetFaultPagedInput): Promise<PagedResultDto<DeviceFaultDto>> {
    const response = await httpClient.get<PagedResultDto<DeviceFaultDto>>('/api/app/device-state/fault-paged-list', {
        params: input,
    })
    return response.data
}

/** 分页查询状态切换日志（需要登录） */
export async function getStateLogPagedListAsync(
    input: GetStateLogPagedInput
): Promise<PagedResultDto<DeviceStateLogDto>> {
    const response = await httpClient.get<PagedResultDto<DeviceStateLogDto>>(
        '/api/app/device-state/state-log-paged-list',
        { params: input }
    )
    return response.data
}

// ─── 显示名称辅助 ─────────────────────────────────────────────────────────────

export const DeviceStatusLabels: Record<DeviceStatus, string> = {
    [DeviceStatus.Standby]: '待机',
    [DeviceStatus.Starting]: '启动中',
    [DeviceStatus.Running]: '运行中',
    [DeviceStatus.Paused]: '已暂停',
    [DeviceStatus.Stopping]: '停止中',
    [DeviceStatus.Stopped]: '已停止',
    [DeviceStatus.Resetting]: '复位中',
    [DeviceStatus.FaultAcknowledging]: '故障确认中',
    [DeviceStatus.Fault]: '故障',
    [DeviceStatus.EmergencyStop]: '急停',
    [DeviceStatus.Initializing]: '初始化中',
}

export const DeviceRunModeLabels: Record<DeviceRunMode, string> = {
    [DeviceRunMode.Online]: '联机',
    [DeviceRunMode.Auto]: '自动',
    [DeviceRunMode.Manual]: '手动',
    [DeviceRunMode.Maintenance]: '检修',
}

export const DeviceFaultLevelLabels: Record<DeviceFaultLevel, string> = {
    [DeviceFaultLevel.Warning]: '警告',
    [DeviceFaultLevel.GeneralFault]: '一般故障',
    [DeviceFaultLevel.SevereFault]: '严重故障',
    [DeviceFaultLevel.SafetyFault]: '安全故障',
}

export const StateChangeTriggerLabels: Record<StateChangeTrigger, string> = {
    [StateChangeTrigger.UserManual]: '用户手动',
    [StateChangeTrigger.SystemAuto]: '系统自动',
    [StateChangeTrigger.DeviceFault]: '设备故障',
    [StateChangeTrigger.EmergencyButton]: '急停按钮',
    [StateChangeTrigger.RemoteCommand]: '远程指令',
    [StateChangeTrigger.Watchdog]: '看门狗',
    [StateChangeTrigger.SystemInit]: '系统初始化',
}
