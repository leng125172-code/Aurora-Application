/**
 * 标定照片管理 REST API（Step 5）
 *
 * 路由前缀：/api/app/calib-photo（ABP 动态 API 约定）
 */
import { httpClient } from '@/api/client'

const BASE = '/api/app/calib-photo'

// ─── 枚举 ────────────────────────────────────────────────────────────────────

/** 标定照片类型 */
export enum CalibPhotoType {
    /** 内参拍照（关灯，拍真实棋盘格） */
    Intrinsic = 0,
    /** 外参拍照（开灯投影棋盘格） */
    Extrinsic = 1,
    /** 双目联合外参成对拍照（左右同步拍真实棋盘格） */
    StereoExtrinsicPair = 2,
}

/** 双目成对照片角色 */
export enum StereoPhotoRole {
    Main = 0,
    Secondary = 1,
}

/** 投影外参双拍阶段 */
export enum ExtrinsicPhotoPhase {
    ProjectorOff = 0,
    ProjectorOn = 1,
    WhiteScreen = 2,
    Checkerboard = 3,
}

/** 标定板类型（与后端 CalibrationBoardType 对齐） */
export enum CalibrationBoardType {
    Chessboard = 0,
    SymmetricCircleGrid = 1,
    AsymmetricCircleGrid = 2,
    MarkedSymmetricCircleGrid = 3,
}

// ─── 接口定义 ─────────────────────────────────────────────────────────────────

/** 标定照片列表项 */
export interface CalibPhotoDto {
    id: string
    photoType: CalibPhotoType
    isValid: boolean
    cornerCountDetected: number
    exposureScore: number | null
    sharpnessScore: number | null
    capturedAt: string
    thumbnailBase64: string | null
    pairGroupId: string | null
    stereoRole: StereoPhotoRole | null
    extrinsicPhase: ExtrinsicPhotoPhase | null
    imageDiffScore: number | null
    imageDiffSignificant: boolean | null
}

/** 标定板参数（读/写） */
export interface CalibBoardConfigDto {
    boardType: CalibrationBoardType
    physicalCornerRows: number
    physicalCornerCols: number
    physicalSquareSizeMm: number
    projectedCornerRows: number
    projectedCornerCols: number
    projectedPixelSize: number
    boardThicknessMm: number
    circleBoardConfig: CircleBoardConfigDto | null
}

export interface CirclePatternSizeDto {
    width: number
    height: number
}

export interface CircleMarkerPositionDto {
    row: number
    col: number
}

export interface CircleBlobDetectorConfigDto {
    minThreshold: number
    maxThreshold: number
    minArea: number
    maxArea: number
    minCircularity: number
    minConvexity: number
}

export interface CircleBoardConfigDto {
    patternSize: CirclePatternSizeDto
    circleSpacing: number
    circleDiameter: number | null
    hasCenterMarker: boolean
    hasCornerLocators: boolean
    markerPosition: CircleMarkerPositionDto
    detector: CircleBlobDetectorConfigDto
}

/** 更新标定板参数输入（CalibProjectId 必填） */
export interface UpdateBoardConfigInput {
    /** 标定项目 ID（会愀入 PUT body，不在路径中） */
    calibProjectId: string
    boardType: CalibrationBoardType
    physicalCornerRows: number
    physicalCornerCols: number
    physicalSquareSizeMm: number
    projectedCornerRows: number
    projectedCornerCols: number
    projectedPixelSize: number
    boardThicknessMm: number
    circleBoardConfig?: CircleBoardConfigDto | null
}

/** 内参拍照输入 */
export interface TakeIntrinsicPhotoInput {
    calibProjectId: string
    cameraDeviceId: string
    /** 投影仪 ID（可选）——传入则后端自动关灯 */
    projectorDeviceId?: string
}

/** 外参拍照输入 */
export interface TakeExtrinsicPhotoInput {
    calibProjectId: string
    cameraDeviceId: string
    stripeImageCount?: number
}

/** 双目联合外参成对拍照输入 */
export interface TakeStereoExtrinsicPairPhotoInput {
    calibProjectId: string
}

/** 双目成对拍照返回 */
export interface CalibStereoPairPhotoDto {
    pairGroupId: string
    mainPhoto: CalibPhotoDto
    secondaryPhoto: CalibPhotoDto
}

/** 标定计算结果 */
export interface CalibComputeResultDto {
    intrinsicMatrixJson: string
    distCoeffsJson: string
    reprojectionError: number
    projectorReprojectionError: number | null
    extrinsicRvecJson: string | null
    extrinsicTvecJson: string | null
    /** 投影仪内参矩阵（3×3，JSON）；无投影仪或未计算时为 null */
    projectorIntrinsicMatrixJson: string | null
    /** 投影仪畸变系数（JSON）；无投影仪或未计算时为 null */
    projectorDistCoeffsJson: string | null
    /** 相机→投影仪旋转矩阵（3×3，JSON）；无投影仪或未计算时为 null */
    cameraToProjectorRJson: string | null
    /** 相机→投影仪平移向量（3×1，JSON）；无投影仪或未计算时为 null */
    cameraToProjectorTJson: string | null
    /** 投影仪标定重投影误差（px）；无投影仪或未计算时为 null */
    projectorCalibReprojectionError: number | null
}

/** 双目联合标定计算结果 */
export interface CalibStereoComputeResultDto {
    /** 本次角点重检成功、首次联合求解使用的照片组数 */
    inputPairCount: number
    /** 排除极线误差离群组后，联合标定最终使用的照片组数 */
    usedPairCount: number
    /** 本次计算按联合模型极线误差排除的照片组；不会改变照片有效状态 */
    rejectedPairs: CalibStereoRejectedPairDto[]
    mainCameraDeviceId: string
    secondaryCameraDeviceId: string
    stereoReprojectionError: number
    rotationMatrixJson: string
    translationVectorJson: string
    transformLtoRJson: string
    transformRtoLJson: string
    rectificationR1Json: string
    rectificationR2Json: string
    projectionP1Json: string
    projectionP2Json: string
    rectifyMapWidth: number
    rectifyMapHeight: number
    map1XBlobKey: string
    map1YBlobKey: string
    map2XBlobKey: string
    map2YBlobKey: string
}

export interface CalibStereoRejectedPairDto {
    pairGroupId: string
    epipolarErrorPx: number
    rejectionThresholdPx: number
    rejectionIteration: number
    reason: string
}

/** 相机标定汇总（照片计数 + 最新结果） */
export interface CalibCameraStatusDto {
    cameraDeviceId: string
    intrinsicTotal: number
    intrinsicValid: number
    extrinsicTotal: number
    extrinsicValid: number
    stereoTotal: number
    stereoValid: number
    latestResult: CalibComputeResultDto | null
}

/** 双目联合标定状态 */
export interface CalibStereoStatusDto {
    pairTotal: number
    pairValid: number
    latestResult: CalibStereoComputeResultDto | null
}

// ─── API 函数 ─────────────────────────────────────────────────────────────────

/**
 * 更新标定项目的棋盘格参数配置
 * ABP 路由: PUT /api/app/calib-photo/board-config（calibProjectId 随 body 传入，不在路径中）
 */
export async function updateBoardConfig(input: UpdateBoardConfigInput): Promise<CalibBoardConfigDto> {
    const res = await httpClient.put<CalibBoardConfigDto>(`${BASE}/board-config`, input)
    return res.data
}

/**
 * 获取标定项目当前棋盘格参数
 * ABP 路由: GET /api/app/calib-photo/board-config/{calibProjectId}
 * 注意：ABP 约定路由会把单个 *Id 参数提升为路径段，而不是 query 参数。
 */
export async function getBoardConfig(calibProjectId: string): Promise<CalibBoardConfigDto> {
    const res = await httpClient.get<CalibBoardConfigDto>(`${BASE}/board-config/${calibProjectId}`)
    return res.data
}

/**
 * 导出标定板参数配置 JSON。
 * ABP 路由: GET /api/app/calib-photo/export-board-config?calibProjectId={id}
 */
export async function exportBoardConfig(calibProjectId: string): Promise<Blob> {
    const res = await httpClient.get(`${BASE}/export-board-config`, {
        params: { calibProjectId },
        responseType: 'blob',
    })
    return res.data as Blob
}

/**
 * 导入标定板参数配置 JSON。
 * ABP 路由: POST /api/app/calib-photo/import-board-config?calibProjectId={id}
 */
export async function importBoardConfig(
    calibProjectId: string,
    file: File,
): Promise<CalibBoardConfigDto> {
    const formData = new FormData()
    formData.append('file', file, file.name)
    const res = await httpClient.post<CalibBoardConfigDto>(`${BASE}/import-board-config`, formData, {
        params: { calibProjectId },
        headers: { 'Content-Type': 'multipart/form-data' },
    })
    return res.data
}

/**
 * 内参拍照（后端自动处理：拍照 → 角点检测 → 存 BLOB）
 */
export async function takeIntrinsicPhoto(
    input: TakeIntrinsicPhotoInput,
    timeout = 30000,
): Promise<CalibPhotoDto> {
    const res = await httpClient.post<CalibPhotoDto>(`${BASE}/take-intrinsic-photo`, input, {
        timeout,
    })
    return res.data
}

/**
 * 外参圆点拍照：投影仪切换到 S1 白屏模式，拍摄圆点标定板。
 * 返回新建的照片记录（含缩略图），用于后续与棋盘格照片配对。
 */
export async function takeExtrinsicDotPhoto(
    input: TakeExtrinsicPhotoInput,
    timeout = 30000,
): Promise<CalibPhotoDto> {
    const res = await httpClient.post<CalibPhotoDto>(`${BASE}/take-extrinsic-dot-photo`, input, {
        timeout,
    })
    return res.data
}

/**
 * 外参棋盘格拍照：投影仪切换到 S3 棋盘格模式，拍摄纯棋盘格（需先移除标定板）。
 * 返回新建的照片记录（含缩略图），与之前拍摄的圆点照片配对为一组外参样本。
 */
export async function takeExtrinsicCheckerboardPhoto(
    input: TakeExtrinsicPhotoInput,
    timeout = 30000,
): Promise<CalibPhotoDto> {
    const res = await httpClient.post<CalibPhotoDto>(`${BASE}/take-extrinsic-checkerboard-photo`, input, {
        timeout,
    })
    return res.data
}

/**
 * 双目联合外参成对拍照（一次采集主/从相机两张）
 */
export async function takeStereoExtrinsicPairPhoto(
    input: TakeStereoExtrinsicPairPhotoInput,
    timeout = 30000,
): Promise<CalibStereoPairPhotoDto> {
    const res = await httpClient.post<CalibStereoPairPhotoDto>(`${BASE}/take-stereo-extrinsic-pair-photo`, input, {
        timeout,
    })
    return res.data
}

/**
 * 获取指定相机的照片列表
 * @param photoType 传 null 则返回全部类型
 */
export async function getPhotoList(
    calibProjectId: string,
    cameraDeviceId: string,
    photoType?: CalibPhotoType,
): Promise<CalibPhotoDto[]> {
    const params: Record<string, unknown> = { calibProjectId, cameraDeviceId }
    if (photoType !== undefined) params.photoType = photoType
    const res = await httpClient.get<CalibPhotoDto[]>(`${BASE}/photo-list`, { params })
    return res.data
}

/**
 * 删除一张照片（同时删除 BLOB 文件）
 */
export async function deletePhoto(photoId: string): Promise<void> {
    await httpClient.delete(`${BASE}/${photoId}`)
}

/**
 * 删除指定相机的全部无效照片
 * ABP 路由: DELETE /api/app/calib-photo/invalid-photos?calibProjectId=...&cameraDeviceId=...
 * @returns 删除的数量
 */
export async function deleteInvalidPhotos(
    calibProjectId: string,
    cameraDeviceId: string,
): Promise<number> {
    const res = await httpClient.delete<number>(`${BASE}/invalid-photos`, {
        params: { calibProjectId, cameraDeviceId },
    })
    return res.data
}

/**
 * 仅计算相机内参（calibrateCamera），结果写入 CalibCameraParam。
 * POST /api/app/calib-photo/compute-intrinsic
 */
export async function computeIntrinsic(
    calibProjectId: string,
    cameraDeviceId: string,
    timeout = 120000,
): Promise<CalibComputeResultDto> {
    const res = await httpClient.post<CalibComputeResultDto>(
        `${BASE}/compute-intrinsic`,
        null,
        { params: { calibProjectId, cameraDeviceId }, timeout },
    )
    return res.data
}

/**
 * 仅计算外参（solvePnP + 投影仪内参），依赖已保存的相机内参。
 * POST /api/app/calib-photo/compute-extrinsic
 */
export async function computeExtrinsic(
    calibProjectId: string,
    cameraDeviceId: string,
    timeout = 120000,
): Promise<CalibComputeResultDto> {
    const res = await httpClient.post<CalibComputeResultDto>(
        `${BASE}/compute-extrinsic`,
        null,
        { params: { calibProjectId, cameraDeviceId }, timeout },
    )
    return res.data
}

/**
 * 计算内外参（calibrateCamera + solvePnP），结果写入 CalibCameraParam
 */
export async function computeCalibration(
    calibProjectId: string,
    cameraDeviceId: string,
    timeout = 120000,
): Promise<CalibComputeResultDto> {
    const res = await httpClient.post<CalibComputeResultDto>(
        `${BASE}/compute-calibration`,
        null,
        { params: { calibProjectId, cameraDeviceId }, timeout },
    )
    return res.data
}

/**
 * 获取指定相机的照片计数及最新标定结果汇总
 */
export async function getCameraStatus(
    calibProjectId: string,
    cameraDeviceId: string,
): Promise<CalibCameraStatusDto> {
    const res = await httpClient.get<CalibCameraStatusDto>(`${BASE}/camera-status`, {
        params: { calibProjectId, cameraDeviceId },
    })
    return res.data
}

/**
 * 计算双目联合外参（StereoCalibrate）
 */
export async function computeStereoCalibration(
    calibProjectId: string,
    timeout = 120000,
): Promise<CalibStereoComputeResultDto> {
    const res = await httpClient.post<CalibStereoComputeResultDto>(
        `${BASE}/compute-stereo-calibration/${calibProjectId}`,
        null,
        { timeout },
    )
    return res.data
}

/**
 * 获取双目联合标定状态（成对样本计数 + 最新结果）
 * ABP 路由: GET /api/app/calib-photo/stereo-status/{calibProjectId}
 */
export async function getStereoStatus(calibProjectId: string): Promise<CalibStereoStatusDto> {
    const res = await httpClient.get<CalibStereoStatusDto>(
        `${BASE}/stereo-status/${calibProjectId}`,
    )
    return res.data
}

/**
 * 校验 Step 5 标定结果是否已全部完成（内参已算；单光还需外参；双目还需双目外参）。
 * ABP 路由: GET /api/app/calib-photo/validate-step5?calibProjectId={id}
 */
export async function validateStep5(calibProjectId: string): Promise<boolean> {
    const res = await httpClient.get<boolean>(`${BASE}/validate-step5`, {
        params: { calibProjectId },
    })
    return res.data
}
