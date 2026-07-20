/**
 * Step3 投影仪参数配置 REST API
 *
 * 后端 AppService：CalibProjectorParamAppService
 * 路由前缀：/api/app/calib-projector-param
 */
import { httpClient } from '@/api/client'

// ===================== DTO 类型 =====================

/** 投影仪参数配置 DTO（回显用） */
export interface CalibProjectorParamDto {
    id: string
    calibProjectId: string
    projectorDeviceId: string
    name: string
    description?: string | null
    /** 投射器分辨率宽度（像素，从投影仪读取） */
    resolutionWidth: number
    /** 投射器分辨率高度（像素，用户输入） */
    resolutionHeight: number
    /** 条纹周期数 */
    periodCount: number
    /** 条纹类型：bw=黑白（首色黑），wb=白黑（首色白） */
    fringeType: 'bw' | 'wb'
    /** 图案数量（相移步数） */
    patternCount: number
    /** 相位偏移量 */
    phaseShift?: number | null
    isEnabled: boolean
    creationTime: string
    lastModificationTime?: string | null
}

/** 保存投影仪参数配置输入 */
export interface SaveCalibProjectorParamInput {
    calibProjectId: string
    projectorDeviceId: string
    resolutionWidth: number
    resolutionHeight: number
    periodCount: number
    fringeType: 'bw' | 'wb'
    patternCount: number
    phaseShift?: number | null
}

// ===================== API 函数 =====================

/**
 * 获取指定标定项目的投影仪参数配置。
 * 若数据库中尚无记录则返回 null，由前端使用默认值。
 * GET /api/app/calib-projector-param/{calibProjectId}
 */
export async function getCalibProjectorParam(
    calibProjectId: string,
): Promise<CalibProjectorParamDto | null> {
    const response = await httpClient.get<CalibProjectorParamDto | null>(
        `/api/app/calib-projector-param/${calibProjectId}`,
    )
    return response.data
}

/**
 * 保存（Upsert）投影仪参数配置。
 * PUT /api/app/calib-projector-param/{calibProjectId}
 */
export async function updateCalibProjectorParam(
    calibProjectId: string,
    input: SaveCalibProjectorParamInput,
): Promise<CalibProjectorParamDto> {
    const response = await httpClient.put<CalibProjectorParamDto>(
        `/api/app/calib-projector-param/${calibProjectId}`,
        input,
    )
    return response.data
}
