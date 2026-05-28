/**
 * 三维数模管理相关 API
 * 端点由 ABP 自动生成，基础路径：/api/app/product-model
 */
import { httpClient } from '@/api/client'

// ===================== 枚举类型 =====================

/** 三维数模文件格式 */
export enum ProductModelFormat {
    /** PLY 点云/网格（无需转换） */
    PLY = 0,
    /** OBJ 网格（无需转换） */
    OBJ = 1,
    /** STEP 工程图（建议转换） */
    STEP = 10,
    /** IGES 工程图（建议转换） */
    IGES = 11,
    /** STL 网格（建议转换） */
    STL = 12,
    /** GLB 3D 场景（有限支持） */
    GLB = 20,
    /** GLTF 3D 场景（有限支持） */
    GLTF = 21,
    /** PCD 点云（有限支持） */
    PCD = 22,
}

/** 转换状态 */
export enum ProductModelConversionStatus {
    /** 无需转换（PLY/OBJ 格式） */
    NotRequired = 0,
    /** 等待转换 */
    Pending = 1,
    /** 转换中 */
    Converting = 2,
    /** 转换成功 */
    Success = 3,
    /** 转换失败 */
    Failed = 4,
}

// ===================== DTO 类型 =====================

export interface ProductModelDto {
    readonly id: string
    readonly name: string
    readonly originalFileName: string
    readonly fileFormat: ProductModelFormat
    readonly fileFormatDisplay: string
    readonly fileSizeBytes: number
    readonly conversionStatus: ProductModelConversionStatus
    readonly conversionErrorMessage: string | null
    readonly isReady: boolean
    /** 本次上传是否已入队转换任务（仅上传响应中为 true，列表查询中始终为 false） */
    readonly needsConversion: boolean
    readonly uploaderUserName: string | null
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface GetProductModelListInput {
    readonly filter?: string | null
    readonly fileFormat?: ProductModelFormat | null
    readonly conversionStatus?: ProductModelConversionStatus | null
    readonly startTime?: string | null
    readonly endTime?: string | null
    readonly uploaderUserId?: string | null
    readonly sorting?: string | null
    readonly skipCount?: number
    readonly maxResultCount?: number
}

export interface PagedResult<T> {
    readonly items: T[]
    readonly totalCount: number
}

export interface UpdateProductModelNameInput {
    readonly name: string
}

// ===================== API 函数 =====================

const BASE = '/api/app/product-model'

/**
 * 获取三维数模分页列表
 */
export async function getProductModelListAsync(input: GetProductModelListInput): Promise<PagedResult<ProductModelDto>> {
    const response = await httpClient.get<PagedResult<ProductModelDto>>(BASE, {
        params: input,
    })
    return response.data
}

/**
 * 获取单个三维数模详情
 */
export async function getProductModelAsync(id: string): Promise<ProductModelDto> {
    const response = await httpClient.get<ProductModelDto>(`${BASE}/${id}`)
    return response.data
}

/**
 * 上传三维数模文件
 * @param file 要上传的文件
 * @param name 可选显示名称（不传则用文件名）
 * @param onProgress 上传进度回调（0-100）
 * @param signal AbortController 信号，用于取消上传
 */
export async function uploadProductModelAsync(
    file: File,
    name?: string | null,
    onProgress?: (percent: number) => void,
    signal?: AbortSignal
): Promise<ProductModelDto> {
    const formData = new FormData()
    formData.append('file', file)
    if (name) {
        formData.append('name', name)
    }
    const response = await httpClient.post<ProductModelDto>(`${BASE}/upload`, formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: (event) => {
            if (onProgress && event.total && event.total > 0) {
                onProgress(Math.round((event.loaded * 100) / event.total))
            }
        },
        signal,
    })
    return response.data
}

/**
 * 重命名三维数模
 */
export async function updateProductModelNameAsync(
    id: string,
    input: UpdateProductModelNameInput
): Promise<ProductModelDto> {
    const response = await httpClient.put<ProductModelDto>(`${BASE}/${id}/name`, input)
    return response.data
}

/**
 * 删除三维数模（同时删除 BLOB 文件）
 */
export async function deleteProductModelAsync(id: string): Promise<void> {
    await httpClient.delete(`${BASE}/${id}`)
}

/**
 * 重试格式转换（仅失败状态可重试）
 */
export async function retryConversionAsync(id: string): Promise<void> {
    await httpClient.post(`${BASE}/${id}/retry-conversion`)
}

/**
 * 下载三维数模文件，返回可用于创建 Object URL 的 Blob
 */
export async function downloadProductModelAsync(id: string): Promise<Blob> {
    const response = await httpClient.get(`${BASE}/${id}/download`, {
        responseType: 'blob',
    })
    return response.data as Blob
}
