/**
 * 标定管理相关 API
 * 端点由 ABP 自动生成：
 *   - 设备：/api/app/calibration-device
 *   - 工程：/api/app/calibration-project
 *   - 结果：/api/app/calibration-result
 */
import { httpClient } from '@/api/client'

// ===================== 枚举 =====================

/** 标定设备拓扑类型 */
export enum CalibrationDeviceType {
    TwoCamZeroLight = 0,
    ThreeCamZeroLight = 1,
    OneCamOneLight = 10,
    TwoCamOneLight = 11,
    ThreeCamOneLight = 12,
}

/** 相机逻辑角色 */
export enum CameraRole {
    Master = 0,
    LeftSlave = 1,
    RightSlave = 2,
    TopMaster = 10,
    LeftBottomSlave = 11,
    RightBottomSlave = 12,
}

/** 电机逻辑角色 */
export enum MotorRole {
    LeftCameraRotate = 0,
    RightCameraRotate = 1,
    SingleCameraRotate = 2,
    BaselineTranslate = 10,
    CameraLightTranslate = 11,
    GimbalGroupTranslate = 12,
    GimbalAxisX = 20,
    GimbalAxisY = 21,
    GimbalAxisZRotate = 22,
    GimbalAxisZTranslate = 23,
}

/** 投射器逻辑角色 */
export enum ProjectorRole {
    MainStructuredLight = 0,
}

/** 标定板类型 */
export enum CalibrationBoardType {
    Chessboard = 0,
    CircleGrid = 1,
    AprilTag = 2,
}

/** CMOS 传感器尺寸 */
export enum CmosSensorSize {
    QuarterInch = 0,
    OneThirdInch = 1,
    OneOverTwoPointThreeInch = 2,
    OneSecondInch = 3,
    TwoThirdInch = 4,
    OneInch = 5,
    FourThirdInch = 6,
    ApsC = 7,
    FullFrame35mm = 8,
    MediumFormat = 9,
    Custom = 99,
}

/** 图像采集像素格式 */
export enum ImageCaptureFormat {
    Raw = 0,
    Bgr = 1,
    Gray = 2,
}

/** 结构光投射图案类型 */
export enum StructuredLightPattern {
    SineFringe = 0,
    RandomSpeckle = 1,
    CodedGrating = 2,
}

/** 标定工程状态 */
export enum CalibrationProjectStatus {
    Draft = 0,
    Configuring = 1,
    Capturing = 2,
    Computing = 3,
    Validating = 4,
    Completed = 5,
    Failed = 6,
    Archived = 7,
}

/** 标定结果导出格式 */
export enum CalibrationResultExportFormat {
    Json = 0,
    Xml = 1,
    Yaml = 2,
    Txt = 3,
}

/** 电机联动限位方向 */
export enum BlockedMotionDirection {
    Positive = 0,
    Negative = 1,
    Both = 2,
}

/** 标定验证类型 */
export enum CalibrationValidationType {
    DistortionCorrection = 0,
    StereoMatching = 1,
    StructuredLightDepth = 2,
    OverallAccuracy = 3,
}

// ===================== 通用 =====================

export interface PagedResult<T> {
    readonly items: T[]
    readonly totalCount: number
}

// ===================== 设备 DTO =====================

export interface CalibrationDeviceListDto {
    readonly id: string
    readonly name: string
    readonly description: string | null
    readonly deviceType: CalibrationDeviceType
    readonly isActive: boolean
    readonly cameraBindingCount: number
    readonly motorBindingCount: number
    readonly projectorBindingCount: number
    readonly hasStructuredLight: boolean
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface CalibrationCameraBindingDto {
    readonly id: string
    readonly calibrationDeviceId: string
    readonly cameraDeviceId: string
    readonly role: CameraRole
}

export interface CalibrationMotorBindingDto {
    readonly id: string
    readonly calibrationDeviceId: string
    readonly motorAxisId: string
    readonly role: MotorRole
    readonly gimbalGroupId: string | null
}

export interface CalibrationProjectorBindingDto {
    readonly id: string
    readonly calibrationDeviceId: string
    readonly projectorDeviceId: string
    readonly role: ProjectorRole
}

export interface CalibrationGimbalPresetDto {
    readonly id: string
    readonly name: string
    readonly remarks: string | null
    readonly xPosition: number
    readonly yPosition: number
    readonly zRotatePosition: number | null
    readonly zTranslatePosition: number | null
}

export interface CalibrationGimbalGroupDto {
    readonly id: string
    readonly name: string
    readonly xAxisMotorId: string
    readonly yAxisMotorId: string
    readonly zRotateAxisMotorId: string | null
    readonly zTranslateAxisMotorId: string | null
    readonly maxVelocity: number
    readonly acceleration: number
    readonly accelDecelTime: number
    readonly presetPositions: CalibrationGimbalPresetDto[]
}

export interface CalibrationMotorInterlockRuleDto {
    readonly id: string
    readonly sourceMotorAxisId: string
    readonly sourcePositionMin: number
    readonly sourcePositionMax: number
    readonly targetMotorAxisId: string
    readonly blockedDirection: BlockedMotionDirection
    readonly isEnabled: boolean
    readonly description: string | null
}

export interface CalibrationCameraParameterDto {
    readonly id: string
    readonly calibrationDeviceId: string
    readonly cameraDeviceId: string
    readonly role: CameraRole
    readonly cmosSize: CmosSensorSize
    readonly cmosWidthMm: number
    readonly cmosHeightMm: number
    readonly resolutionWidthPx: number
    readonly resolutionHeightPx: number
    readonly nominalFocalLengthMm: number
    readonly maxAperture: number
    readonly minAperture: number
    readonly currentAperture: number
    readonly minExposureUs: number
    readonly maxExposureUs: number
    readonly minGainDb: number
    readonly maxGainDb: number
    readonly pixelSizeUm: number
}

export interface CalibrationProjectorParameterDto {
    readonly id: string
    readonly calibrationDeviceId: string
    readonly projectorDeviceId: string
    readonly resolutionWidthPx: number
    readonly resolutionHeightPx: number
    readonly throwRatio: number
    readonly minWorkingDistanceMm: number
    readonly maxWorkingDistanceMm: number
    readonly pattern: StructuredLightPattern
    readonly patternCount: number
    readonly phaseShift: number
}

export interface CalibrationMotorParameterDto {
    readonly id: string
    readonly calibrationDeviceId: string
    readonly motorAxisId: string
    readonly role: MotorRole
    readonly encoderResolution: number
    readonly gearRatio: number
    readonly homePosition: number
    readonly homeDirection: number
    readonly homingVelocity: number
    readonly homingAcceleration: number
    readonly softLimitMin: number
    readonly softLimitMax: number
    readonly isHomed: boolean
    readonly lastHomedTime: string | null
}

export interface CalibrationDeviceDetailDto extends CalibrationDeviceListDto {
    readonly cameraBindings: CalibrationCameraBindingDto[]
    readonly motorBindings: CalibrationMotorBindingDto[]
    readonly projectorBindings: CalibrationProjectorBindingDto[]
    readonly gimbalGroups: CalibrationGimbalGroupDto[]
    readonly interlockRules: CalibrationMotorInterlockRuleDto[]
    readonly cameraParameters: CalibrationCameraParameterDto[]
    readonly projectorParameters: CalibrationProjectorParameterDto[]
    readonly motorParameters: CalibrationMotorParameterDto[]
}

// ===================== 工程 DTO =====================

export interface CalibrationProjectListDto {
    readonly id: string
    readonly name: string
    readonly description: string | null
    readonly calibrationDeviceId: string
    readonly status: CalibrationProjectStatus
    readonly startedTime: string | null
    readonly completedTime: string | null
    readonly frameCount: number
    readonly acceptedFrameCount: number
    readonly targetCaptureCount: number
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface CalibrationCaptureImageDto {
    readonly id: string
    readonly calibrationCaptureFrameId: string
    readonly cameraDeviceId: string
    readonly cameraRole: CameraRole
    readonly blobName: string
    readonly width: number
    readonly height: number
    readonly fileSizeBytes: number
    readonly reprojectionError: number | null
}

export interface CalibrationCaptureFrameDto {
    readonly id: string
    readonly calibrationProjectId: string
    readonly frameIndex: number
    readonly capturedTime: string
    readonly isAccepted: boolean
    readonly rejectionReason: string | null
    readonly images: CalibrationCaptureImageDto[]
}

export interface CalibrationProjectDetailDto extends CalibrationProjectListDto {
    readonly failureReason: string | null
    readonly boardType: CalibrationBoardType
    readonly boardRows: number
    readonly boardCols: number
    readonly squareSizeMm: number | null
    readonly circleDiameterMm: number | null
    readonly circleSpacingMm: number | null
    readonly aprilTagFamily: string | null
    readonly aprilTagSizeMm: number | null
    readonly aprilTagSpacingMm: number | null
    readonly boardManufactureAccuracyMm: number
    readonly unifiedExposureUs: number
    readonly unifiedGainDb: number
    readonly unifiedWhiteBalance: string | null
    readonly imageFormat: ImageCaptureFormat
    readonly structuredLightBrightness: number | null
    readonly patternIntervalMs: number | null
    readonly capturesPerPhase: number | null
    readonly frames: CalibrationCaptureFrameDto[]
}

// ===================== 结果 DTO =====================

export interface CalibrationValidationRecordDto {
    readonly id: string
    readonly calibrationResultId: string
    readonly validationType: CalibrationValidationType
    readonly isPassed: boolean
    readonly metricsJson: string | null
    readonly reportBlobName: string | null
    readonly remarks: string | null
    readonly validatedTime: string
}

export interface CalibrationResultListDto {
    readonly id: string
    readonly calibrationProjectId: string
    readonly calibrationDeviceId: string
    readonly version: number
    readonly isActive: boolean
    readonly computedTime: string
    readonly overallReprojectionError: number
    readonly rmsError: number
    readonly validationCount: number
    readonly creationTime: string
}

export interface CalibrationResultDetailDto extends CalibrationResultListDto {
    readonly cameraIntrinsicsJson: string
    readonly cameraExtrinsicsJson: string
    readonly structuredLightCalibrationJson: string | null
    readonly maxError: number
    readonly minError: number
    readonly meanError: number
    readonly validations: CalibrationValidationRecordDto[]
}

// ===================== 输入类型 =====================

export interface CreateUpdateCalibrationDeviceDto {
    name: string
    description?: string | null
    deviceType: CalibrationDeviceType
}

export interface CreateCalibrationProjectDto {
    name: string
    calibrationDeviceId: string
    description?: string | null
}

export interface UpdateCalibrationProjectDto {
    name: string
    description?: string | null
}

export interface SetBoardConfigInput {
    boardType: CalibrationBoardType
    rows: number
    cols: number
    manufactureAccuracyMm: number
    squareSizeMm?: number | null
    circleDiameterMm?: number | null
    circleSpacingMm?: number | null
    aprilTagFamily?: string | null
    aprilTagSizeMm?: number | null
    aprilTagSpacingMm?: number | null
}

export interface SetCaptureConfigInput {
    targetCaptureCount: number
    unifiedExposureUs: number
    unifiedGainDb: number
    imageFormat: ImageCaptureFormat
    unifiedWhiteBalance?: string | null
    structuredLightBrightness?: number | null
    patternIntervalMs?: number | null
    capturesPerPhase?: number | null
}

export interface CreateUpdateCameraParameterInput {
    cameraDeviceId: string
    role: CameraRole
    cmosSize: CmosSensorSize
    cmosWidthMm: number
    cmosHeightMm: number
    resolutionWidthPx: number
    resolutionHeightPx: number
    nominalFocalLengthMm: number
    maxAperture: number
    minAperture: number
    currentAperture: number
    minExposureUs: number
    maxExposureUs: number
    minGainDb: number
    maxGainDb: number
}

export interface GetCalibrationDeviceListInput {
    filter?: string | null
    deviceType?: CalibrationDeviceType | null
    isActive?: boolean | null
    skipCount?: number
    maxResultCount?: number
    sorting?: string | null
}

export interface GetCalibrationProjectListInput {
    filter?: string | null
    calibrationDeviceId?: string | null
    status?: CalibrationProjectStatus | null
    skipCount?: number
    maxResultCount?: number
    sorting?: string | null
}

export interface GetCalibrationResultListInput {
    calibrationProjectId?: string | null
    calibrationDeviceId?: string | null
    isActive?: boolean | null
    skipCount?: number
    maxResultCount?: number
}

// ===================== 路由常量 =====================

const DEVICE_BASE = '/api/app/calibration-device'
const PROJECT_BASE = '/api/app/calibration-project'
const RESULT_BASE = '/api/app/calibration-result'

// ===================== 设备 API =====================

/** 获取标定设备列表 */
export async function getCalibrationDeviceListAsync(
    input: GetCalibrationDeviceListInput = {}
): Promise<PagedResult<CalibrationDeviceListDto>> {
    const res = await httpClient.get<PagedResult<CalibrationDeviceListDto>>(DEVICE_BASE, { params: input })
    return res.data
}

/** 获取标定设备详情 */
export async function getCalibrationDeviceAsync(id: string): Promise<CalibrationDeviceDetailDto> {
    const res = await httpClient.get<CalibrationDeviceDetailDto>(`${DEVICE_BASE}/${id}`)
    return res.data
}

/** 新建标定设备 */
export async function createCalibrationDeviceAsync(
    input: CreateUpdateCalibrationDeviceDto
): Promise<CalibrationDeviceDetailDto> {
    const res = await httpClient.post<CalibrationDeviceDetailDto>(DEVICE_BASE, input)
    return res.data
}

/** 更新标定设备基础信息 */
export async function updateCalibrationDeviceAsync(
    id: string,
    input: CreateUpdateCalibrationDeviceDto
): Promise<CalibrationDeviceDetailDto> {
    const res = await httpClient.put<CalibrationDeviceDetailDto>(`${DEVICE_BASE}/${id}`, input)
    return res.data
}

/** 删除标定设备 */
export async function deleteCalibrationDeviceAsync(id: string): Promise<void> {
    await httpClient.delete(`${DEVICE_BASE}/${id}`)
}

/** 启用/禁用标定设备 */
export async function setCalibrationDeviceActiveAsync(
    id: string,
    isActive: boolean
): Promise<CalibrationDeviceDetailDto> {
    const res = await httpClient.post<CalibrationDeviceDetailDto>(
        `${DEVICE_BASE}/${id}/set-active`,
        { isActive }
    )
    return res.data
}

/** 新增/更新相机绑定 */
export async function upsertCameraBindingAsync(
    deviceId: string,
    cameraDeviceId: string,
    role: CameraRole
): Promise<CalibrationCameraBindingDto> {
    const res = await httpClient.post<CalibrationCameraBindingDto>(
        `${DEVICE_BASE}/upsert-camera-binding`,
        { cameraDeviceId, role },
        { params: { deviceId } }
    )
    return res.data
}

/** 删除相机绑定 */
export async function removeCameraBindingAsync(deviceId: string, bindingId: string): Promise<void> {
    await httpClient.delete(`${DEVICE_BASE}/remove-camera-binding`, {
        params: { deviceId, bindingId },
    })
}

/** 新增/更新电机绑定 */
export async function upsertMotorBindingAsync(
    deviceId: string,
    motorAxisId: string,
    role: MotorRole,
    gimbalGroupId?: string | null
): Promise<CalibrationMotorBindingDto> {
    const res = await httpClient.post<CalibrationMotorBindingDto>(
        `${DEVICE_BASE}/upsert-motor-binding`,
        { motorAxisId, role, gimbalGroupId },
        { params: { deviceId } }
    )
    return res.data
}

/** 新增/更新相机硬件参数 */
export async function upsertCameraParameterAsync(
    deviceId: string,
    input: CreateUpdateCameraParameterInput
): Promise<CalibrationCameraParameterDto> {
    const res = await httpClient.post<CalibrationCameraParameterDto>(
        `${DEVICE_BASE}/upsert-camera-parameter`,
        input,
        { params: { deviceId } }
    )
    return res.data
}

// ===================== 工程 API =====================

/** 获取标定工程列表 */
export async function getCalibrationProjectListAsync(
    input: GetCalibrationProjectListInput = {}
): Promise<PagedResult<CalibrationProjectListDto>> {
    const res = await httpClient.get<PagedResult<CalibrationProjectListDto>>(PROJECT_BASE, { params: input })
    return res.data
}

/** 获取标定工程详情 */
export async function getCalibrationProjectAsync(id: string): Promise<CalibrationProjectDetailDto> {
    const res = await httpClient.get<CalibrationProjectDetailDto>(`${PROJECT_BASE}/${id}`)
    return res.data
}

/** 新建标定工程 */
export async function createCalibrationProjectAsync(
    input: CreateCalibrationProjectDto
): Promise<CalibrationProjectDetailDto> {
    const res = await httpClient.post<CalibrationProjectDetailDto>(PROJECT_BASE, input)
    return res.data
}

/** 更新标定工程基础信息 */
export async function updateCalibrationProjectAsync(
    id: string,
    input: UpdateCalibrationProjectDto
): Promise<CalibrationProjectDetailDto> {
    const res = await httpClient.put<CalibrationProjectDetailDto>(`${PROJECT_BASE}/${id}`, input)
    return res.data
}

/** 删除标定工程 */
export async function deleteCalibrationProjectAsync(id: string): Promise<void> {
    await httpClient.delete(`${PROJECT_BASE}/${id}`)
}

/** 设置标定板参数 */
export async function setBoardConfigAsync(
    id: string,
    input: SetBoardConfigInput
): Promise<CalibrationProjectDetailDto> {
    const res = await httpClient.post<CalibrationProjectDetailDto>(
        `${PROJECT_BASE}/${id}/set-board-config`,
        input
    )
    return res.data
}

/** 设置采集参数 */
export async function setCaptureConfigAsync(
    id: string,
    input: SetCaptureConfigInput
): Promise<CalibrationProjectDetailDto> {
    const res = await httpClient.post<CalibrationProjectDetailDto>(
        `${PROJECT_BASE}/${id}/set-capture-config`,
        input
    )
    return res.data
}

/** 切换工程状态 */
export async function transitionProjectStatusAsync(
    id: string,
    status: CalibrationProjectStatus,
    failureReason?: string | null
): Promise<CalibrationProjectDetailDto> {
    const res = await httpClient.post<CalibrationProjectDetailDto>(
        `${PROJECT_BASE}/${id}/transition-status`,
        { status, failureReason }
    )
    return res.data
}

/** 触发单次同步采集 */
export async function captureFrameAsync(id: string): Promise<CalibrationCaptureFrameDto> {
    const res = await httpClient.post<CalibrationCaptureFrameDto>(`${PROJECT_BASE}/${id}/capture-frame`)
    return res.data
}

/** 接受采集帧 */
export async function acceptFrameAsync(projectId: string, frameId: string): Promise<void> {
    await httpClient.post(`${PROJECT_BASE}/accept-frame`, null, { params: { projectId, frameId } })
}

/** 拒绝采集帧 */
export async function rejectFrameAsync(
    projectId: string,
    frameId: string,
    reason: string
): Promise<void> {
    await httpClient.post(
        `${PROJECT_BASE}/reject-frame`,
        { reason },
        { params: { projectId, frameId } }
    )
}

/** 删除采集帧 */
export async function deleteFrameAsync(projectId: string, frameId: string): Promise<void> {
    await httpClient.delete(`${PROJECT_BASE}/delete-frame`, { params: { projectId, frameId } })
}

/** 触发标定计算（入队 Hangfire Job） */
export async function computeCalibrationAsync(id: string): Promise<void> {
    await httpClient.post(`${PROJECT_BASE}/${id}/compute`)
}

// ===================== 结果 API =====================

/** 获取标定结果列表 */
export async function getCalibrationResultListAsync(
    input: GetCalibrationResultListInput = {}
): Promise<PagedResult<CalibrationResultListDto>> {
    const res = await httpClient.get<PagedResult<CalibrationResultListDto>>(RESULT_BASE, { params: input })
    return res.data
}

/** 获取标定结果详情 */
export async function getCalibrationResultAsync(id: string): Promise<CalibrationResultDetailDto> {
    const res = await httpClient.get<CalibrationResultDetailDto>(`${RESULT_BASE}/${id}`)
    return res.data
}

/** 将结果设为生效版本 */
export async function setCalibrationResultActiveAsync(id: string): Promise<CalibrationResultDetailDto> {
    const res = await httpClient.post<CalibrationResultDetailDto>(`${RESULT_BASE}/${id}/set-active`)
    return res.data
}

/** 导出标定结果（返回 Blob，供前端触发下载） */
export async function exportCalibrationResultAsync(
    id: string,
    format: CalibrationResultExportFormat
): Promise<Blob> {
    const res = await httpClient.post(`${RESULT_BASE}/${id}/export`, null, {
        params: { format },
        responseType: 'blob',
    })
    return res.data as Blob
}
