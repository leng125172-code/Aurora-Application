/**
 * Step7 点云生成 REST API
 *
 * 路由前缀：/api/app/calib-point-cloud（ABP 动态 API 约定）
 */
import { httpClient } from '@/api/client'

const BASE = '/api/app/calib-point-cloud'

/** Step7 点云生成运行状态 */
export enum PointCloudRunState {
  Idle = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
}

/** 触发点云生成输入 */
export interface GeneratePointCloudInput {
  calibProjectId: string
}

/** 点云生成状态 */
export interface PointCloudStatusDto {
  calibProjectId: string
  state: PointCloudRunState
  isRunning: boolean
  progress: number
  progressMessage: string | null
  startedAt: string | null
  lastUpdatedAt: string
  errorMessage: string | null
  plyDownloadUrl: string | null
  plyFileSizeBytes: number | null
}

/** 触发点云生成 */
export async function generatePointCloud(input: GeneratePointCloudInput): Promise<PointCloudStatusDto> {
  const res = await httpClient.post<PointCloudStatusDto>(`${BASE}/generate`, input)
  return res.data
}

/** 取消点云生成 */
export async function cancelPointCloud(calibProjectId: string): Promise<PointCloudStatusDto> {
  const res = await httpClient.post<PointCloudStatusDto>(`${BASE}/cancel`, null, {
    params: { calibProjectId },
  })
  return res.data
}

/** 获取点云生成状态 */
export async function getPointCloudStatus(calibProjectId: string): Promise<PointCloudStatusDto> {
  const res = await httpClient.get<PointCloudStatusDto>(`${BASE}/status/${calibProjectId}`)
  return res.data
}

/** 使用当前登录令牌下载 PLY，避免原生链接请求丢失 Authorization 头。 */
export async function downloadPointCloud(calibProjectId: string): Promise<Blob> {
  const res = await httpClient.get<Blob>(`${BASE}/download/${calibProjectId}`, {
    responseType: 'blob',
    timeout: 120_000,
  })
  return res.data
}
