/**
 * 标定模块相关 API
 * 端点由 ABP 自动生成，基础路径：
 *   /api/app/calib-project       — 设备标定项目
 *   /api/app/calib-gimbal-group  — 云台组（标定结构）
 */
import { httpClient } from '@/api/client'

// ===================== 枚举类型 =====================

/** 设备系列 */
export enum DeviceSeries {
    /** 无光系列 */
    NoLight = 0,
    /** 单光系列 */
    SingleLight = 1,
}

/** 设备类型 */
export enum CalibDeviceType {
    TwoCamera0Light = 0,
    ThreeCamera0Light = 1,
    OneCamera1Light = 2,
    TwoCamera1Light = 3,
    ThreeCamera1Light = 4,
}

/** 标定流程状态 */
export enum CalibStatus {
    Initializing = 0,
    MotorParamConfig = 1,
    MotorConstraintConfig = 2,
    GimbalConfig = 3,
    CameraParamConfig = 4,
    ProjectorParamConfig = 5,
    DeviceBinding = 6,
    Completed = 7,
}

/** 标定电机类型。 */
export enum CalibMotorType {
    Rotation = 0,
    Distance = 1,
}

/** 回原方向。 */
export enum OriginDirection {
    Positive = 0,
    Negative = 1,
}

// ===================== 标定项目 DTO =====================

export interface CalibProjectDto {
    readonly id: string
    readonly name: string
    readonly description: string | null
    readonly deviceSeries: DeviceSeries
    readonly deviceType: CalibDeviceType
    readonly cameraCount: number
    readonly projectorCount: number
    readonly calibStatus: CalibStatus
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface GetCalibProjectListInput {
    filter?: string | null
    deviceSeries?: DeviceSeries | null
    deviceType?: CalibDeviceType | null
    calibStatus?: CalibStatus | null
    startTime?: string | null
    endTime?: string | null
    sorting?: string | null
    skipCount?: number
    maxResultCount?: number
}

export interface CreateCalibProjectInput {
    name: string
    description?: string | null
    deviceType: CalibDeviceType
}

export interface UpdateCalibProjectInput {
    name: string
    description?: string | null
}

export interface PagedResult<T> {
    readonly items: T[]
    readonly totalCount: number
}

// ===================== 标定项目 API =====================

const CALIB_PROJECT_BASE = '/api/app/calib-project'

/** 分页查询标定项目列表 */
export async function getCalibProjectListAsync(
    input: GetCalibProjectListInput
): Promise<PagedResult<CalibProjectDto>> {
    const response = await httpClient.get<PagedResult<CalibProjectDto>>(CALIB_PROJECT_BASE, {
        params: input,
    })
    return response.data
}

/** 获取单个标定项目 */
export async function getCalibProjectAsync(id: string): Promise<CalibProjectDto> {
    const response = await httpClient.get<CalibProjectDto>(`${CALIB_PROJECT_BASE}/${id}`)
    return response.data
}

/** 新增标定项目 */
export async function createCalibProjectAsync(input: CreateCalibProjectInput): Promise<CalibProjectDto> {
    const response = await httpClient.post<CalibProjectDto>(CALIB_PROJECT_BASE, input)
    return response.data
}

/** 修改标定项目名称/描述 */
export async function updateCalibProjectAsync(
    id: string,
    input: UpdateCalibProjectInput
): Promise<CalibProjectDto> {
    const response = await httpClient.put<CalibProjectDto>(`${CALIB_PROJECT_BASE}/${id}`, input)
    return response.data
}

/** 删除标定项目 */
export async function deleteCalibProjectAsync(id: string): Promise<void> {
    await httpClient.delete(`${CALIB_PROJECT_BASE}/${id}`)
}

// ===================== 云台组 DTO =====================

export interface CalibGimbalGroupDto {
    readonly id: string
    readonly name: string
    readonly description: string | null
    readonly maxSpeed: number
    readonly acceleration: number
    readonly accelerationTime: number
    readonly decelerationTime: number
    readonly isEnabled: boolean
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface GetCalibGimbalGroupListInput {
    filter?: string | null
    isEnabled?: boolean | null
    startTime?: string | null
    endTime?: string | null
    sorting?: string | null
    skipCount?: number
    maxResultCount?: number
}

export interface CreateUpdateCalibGimbalGroupInput {
    name: string
    description?: string | null
    maxSpeed: number
    acceleration: number
    accelerationTime: number
    decelerationTime: number
    isEnabled: boolean
}

// ===================== 标定电机参数 DTO =====================

export interface CalibMotorParamDto {
    readonly id: string
    readonly calibProjectId: string
    readonly motorAxisId: string
    readonly motorType: CalibMotorType
    readonly encoderResolution: number
    readonly gearRatio: number
    readonly mechanicalOriginPosition: number
    readonly originDirection: OriginDirection
    readonly positiveSoftLimit: number
    readonly negativeSoftLimit: number
    readonly homeSpeed: number
    readonly homeAcceleration: number
    readonly isOriginLocked: boolean
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface SaveCalibMotorParamInput {
    calibProjectId: string
    motorAxisId: string
    motorType: CalibMotorType
    encoderResolution?: number | null
    gearRatio?: number | null
    mechanicalOriginPosition?: number | null
    originDirection?: OriginDirection | null
    positiveSoftLimit?: number | null
    negativeSoftLimit?: number | null
    homeSpeed?: number | null
    homeAcceleration?: number | null
    isOriginLocked?: boolean | null
}

// ===================== 云台组 API =====================

const GIMBAL_GROUP_BASE = '/api/app/calib-gimbal-group'
const CALIB_MOTOR_PARAM_BASE = '/api/app/calib-motor-param'

/** 分页查询云台组列表 */
export async function getCalibGimbalGroupListAsync(
    input: GetCalibGimbalGroupListInput
): Promise<PagedResult<CalibGimbalGroupDto>> {
    const response = await httpClient.get<PagedResult<CalibGimbalGroupDto>>(GIMBAL_GROUP_BASE, {
        params: input,
    })
    return response.data
}

/** 获取单个云台组 */
export async function getCalibGimbalGroupAsync(id: string): Promise<CalibGimbalGroupDto> {
    const response = await httpClient.get<CalibGimbalGroupDto>(`${GIMBAL_GROUP_BASE}/${id}`)
    return response.data
}

/** 新增云台组 */
export async function createCalibGimbalGroupAsync(
    input: CreateUpdateCalibGimbalGroupInput
): Promise<CalibGimbalGroupDto> {
    const response = await httpClient.post<CalibGimbalGroupDto>(GIMBAL_GROUP_BASE, input)
    return response.data
}

/** 修改云台组 */
export async function updateCalibGimbalGroupAsync(
    id: string,
    input: CreateUpdateCalibGimbalGroupInput
): Promise<CalibGimbalGroupDto> {
    const response = await httpClient.put<CalibGimbalGroupDto>(`${GIMBAL_GROUP_BASE}/${id}`, input)
    return response.data
}

/** 删除云台组 */
export async function deleteCalibGimbalGroupAsync(id: string): Promise<void> {
    await httpClient.delete(`${GIMBAL_GROUP_BASE}/${id}`)
}

/** 获取标定项目的所有电机参数。 */
export async function getCalibMotorParamListAsync(
    calibProjectId: string
): Promise<CalibMotorParamDto[]> {
    const response = await httpClient.get<CalibMotorParamDto[]>(CALIB_MOTOR_PARAM_BASE, {
        params: { calibProjectId },
    })
    return response.data
}

/** 保存（Upsert）电机参数。 */
export async function saveCalibMotorParamAsync(
    input: SaveCalibMotorParamInput
): Promise<CalibMotorParamDto> {
    const response = await httpClient.post<CalibMotorParamDto>(`${CALIB_MOTOR_PARAM_BASE}/save`, input)
    return response.data
}

/** 删除指定电机参数记录。 */
export async function deleteCalibMotorParamAsync(id: string): Promise<void> {
    await httpClient.delete(`${CALIB_MOTOR_PARAM_BASE}/${id}`)
}

// ===================== 标定相机参数 =====================

/** CMOS 传感器尺寸选项 */
export interface CmosSensorSize {
    /** 存储在数据库的稳定代码（不随语言变化） */
    code: string
    /** i18n 翻译键，格式为 'calib.sensorSize_xxx' */
    labelKey: string
    /** 传感器宽度 mm */
    widthMm: number
    /** 传感器高度 mm */
    heightMm: number
}

/** 内置 CMOS 传感器尺寸参照表 */
export const CMOS_SENSOR_SIZES: CmosSensorSize[] = [
    { code: '1/4"', labelKey: 'calib.sensorSize_1_4', widthMm: 3.2, heightMm: 2.4 },
    { code: '1/3"', labelKey: 'calib.sensorSize_1_3', widthMm: 4.8, heightMm: 3.6 },
    { code: '1/2.3"', labelKey: 'calib.sensorSize_1_2_3', widthMm: 6.17, heightMm: 4.55 },
    { code: '1/2"', labelKey: 'calib.sensorSize_1_2', widthMm: 6.4, heightMm: 4.8 },
    { code: '2/3"', labelKey: 'calib.sensorSize_2_3', widthMm: 8.8, heightMm: 6.6 },
    { code: '1"', labelKey: 'calib.sensorSize_1', widthMm: 13.2, heightMm: 8.8 },
    { code: '4/3"', labelKey: 'calib.sensorSize_4_3', widthMm: 17.3, heightMm: 13.0 },
    { code: 'APS-C', labelKey: 'calib.sensorSize_apsc', widthMm: 23.5, heightMm: 15.6 },
    { code: 'FF-35mm', labelKey: 'calib.sensorSize_ff', widthMm: 36.0, heightMm: 24.0 },
    { code: 'MF', labelKey: 'calib.sensorSize_mf', widthMm: 53.7, heightMm: 40.2 },
]

/** 标定相机参数 DTO（字段名与后端 C# 属性名 camelCase 一致） */
export interface CalibCameraParamDto {
    readonly id: string
    readonly calibProjectId: string
    readonly cameraDeviceId: string
    readonly name: string
    readonly description: string | null
    readonly isEnabled: boolean
    /** CMOS 传感器尺寸描述 */
    readonly sensorSize: string | null
    readonly sensorWidthMm: number | null
    readonly sensorHeightMm: number | null
    readonly imageWidthPixels: number | null
    readonly imageHeightPixels: number | null
    readonly pixelSizeUm: number | null
    readonly lensFocalLength: number | null
    readonly maxAperture: number | null
    readonly minAperture: number | null
    readonly currentAperture: number | null
    readonly exposureTimeMinUs: number | null
    readonly exposureTimeMaxUs: number | null
    readonly gainMinDb: number | null
    readonly gainMaxDb: number | null
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface SaveCalibCameraParamInput {
    calibProjectId: string
    cameraDeviceId: string
    name: string
    description?: string | null
    isEnabled: boolean
    sensorSize?: string | null
    sensorWidthMm?: number | null
    sensorHeightMm?: number | null
    imageWidthPixels?: number | null
    imageHeightPixels?: number | null
    lensFocalLength?: number | null
    maxAperture?: number | null
    minAperture?: number | null
    currentAperture?: number | null
    exposureTimeMinUs?: number | null
    exposureTimeMaxUs?: number | null
    gainMinDb?: number | null
    gainMaxDb?: number | null
}

const CALIB_CAMERA_PARAM_BASE = '/api/app/calib-camera-param'

/** 获取标定项目的所有相机参数 */
export async function getCalibCameraParamListAsync(
    calibProjectId: string
): Promise<CalibCameraParamDto[]> {
    const response = await httpClient.get<CalibCameraParamDto[]>(CALIB_CAMERA_PARAM_BASE, {
        params: { calibProjectId },
    })
    return response.data
}

/** 保存（Upsert）相机参数，后端按 calibProjectId+cameraDeviceId 幂等处理 */
export async function saveCalibCameraParamAsync(
    input: SaveCalibCameraParamInput
): Promise<CalibCameraParamDto> {
    const response = await httpClient.post<CalibCameraParamDto>(
        `${CALIB_CAMERA_PARAM_BASE}/save`,
        input
    )
    return response.data
}

