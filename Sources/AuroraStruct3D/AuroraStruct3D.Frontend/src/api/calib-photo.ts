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
}

// ─── 接口定义 ─────────────────────────────────────────────────────────────────

/** 标定照片列表项 */
export interface CalibPhotoDto {
  id: string
  photoType: CalibPhotoType
  isValid: boolean
  cornerCountDetected: number
  capturedAt: string
  thumbnailBase64: string | null
}

/** 标定板参数（读/写） */
export interface CalibBoardConfigDto {
  physicalCornerRows: number
  physicalCornerCols: number
  physicalSquareSizeMm: number
  projectedCornerRows: number
  projectedCornerCols: number
  projectedPixelSize: number
}

/** 更新标定板参数输入（CalibProjectId 必填） */
export interface UpdateBoardConfigInput {
  /** 标定项目 ID（会愀入 PUT body，不在路径中） */
  calibProjectId: string
  physicalCornerRows: number
  physicalCornerCols: number
  physicalSquareSizeMm: number
  projectedCornerRows: number
  projectedCornerCols: number
  projectedPixelSize: number
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
  projectorDeviceId: string
}

/** 标定计算结果 */
export interface CalibComputeResultDto {
  intrinsicMatrixJson: string
  distCoeffsJson: string
  reprojectionError: number
  extrinsicRvecJson: string | null
  extrinsicTvecJson: string | null
}

/** 相机标定汇总（照片计数 + 最新结果） */
export interface CalibCameraStatusDto {
  cameraDeviceId: string
  intrinsicTotal: number
  intrinsicValid: number
  extrinsicTotal: number
  extrinsicValid: number
  latestResult: CalibComputeResultDto | null
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
 * ABP 路由: GET /api/app/calib-photo/board-config?calibProjectId=...（非 id 字段走 query string）
 */
export async function getBoardConfig(calibProjectId: string): Promise<CalibBoardConfigDto> {
  const res = await httpClient.get<CalibBoardConfigDto>(`${BASE}/board-config`, {
    params: { calibProjectId },
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
 * 外参拍照（后端自动处理：开灯 → 投影棋盘图 → 拍照 → 关灯 → 角点检测 → 存 BLOB）
 */
export async function takeExtrinsicPhoto(
  input: TakeExtrinsicPhotoInput,
  timeout = 30000,
): Promise<CalibPhotoDto> {
  const res = await httpClient.post<CalibPhotoDto>(`${BASE}/take-extrinsic-photo`, input, {
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
 * 计算内外参（calibrateCamera + solvePnP），结果写入 CalibCameraParam
 */
export async function computeCalibration(
  calibProjectId: string,
  cameraDeviceId: string,
  timeout = 120000,
): Promise<CalibComputeResultDto> {
  const res = await httpClient.post<CalibComputeResultDto>(
    `${BASE}/compute`,
    { calibProjectId, cameraDeviceId },
    { timeout },
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
