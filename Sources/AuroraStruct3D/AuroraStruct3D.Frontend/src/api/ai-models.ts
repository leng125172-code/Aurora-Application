import { httpClient } from '@/api/client'

export enum AiModelLoadStatus {
    Unloaded = 0,
    Loading = 1,
    Loaded = 2,
    Failed = 3,
}

export enum AiModelOperationType {
    Upload = 0,
    Update = 1,
    Load = 2,
    Unload = 3,
    Delete = 4,
    InferenceRequest = 5,
    CleanUpOrphanedRecords = 6,
    StartConversion = 7,
    DeleteConvertedFile = 8,
}

export enum AiModelConversionPreference {
    Auto = 0,
    DirectOnnx = 1,
    ToRknn = 2,
    ToRkllm = 3,
}

export enum AiModelResolvedConversionType {
    Unknown = 0,
    DirectOnnx = 1,
    ToRknn = 2,
    ToRkllm = 3,
}

export enum AiModelFileRole {
    SingleWholeModel = 0,
    SplitEncoder = 1,
    SplitDecoder = 2,
}

export enum AiModelFileConversionStatus {
    None = 0,
    Pending = 1,
    Converting = 2,
    Completed = 3,
    Failed = 4,
}

export interface AiModelIdentifierDto {
    readonly id: string
    readonly name: string
}

export interface AiModelFileDto {
    readonly id: string
    readonly aiModelId: string
    readonly originalFileName: string
    readonly displayName: string
    readonly fileFormat: string
    readonly fileSizeBytes: number
    readonly md5: string
    readonly fileRole: AiModelFileRole
    readonly md5Verified: boolean
    readonly sortOrder: number
    readonly isOriginalFile: boolean
    readonly isConvertedFile: boolean
    readonly sourceFileId?: string | null
    readonly conversionTargetType?: AiModelResolvedConversionType | null
    readonly conversionStatus: AiModelFileConversionStatus
    readonly conversionErrorMessage: string | null
    readonly conversionTime?: string | null
    readonly creatorId?: string | null
    readonly creationTime: string
    readonly lastModificationTime?: string | null
}

export interface AiModelConversionStateDto {
    readonly modelId: string
    readonly sourceFileIds: string[]
    readonly targetType?: AiModelResolvedConversionType | null
    readonly status: AiModelFileConversionStatus
    readonly conversionErrorMessage: string | null
    readonly lastUpdatedTime?: string | null
}

export interface AiModelConversionStartResultDto {
    readonly modelId: string
    readonly conversionPreference: AiModelConversionPreference
    readonly resolvedConversionType: AiModelResolvedConversionType
    readonly canConvert: boolean
    readonly queued: boolean
    readonly status: AiModelFileConversionStatus
    readonly message: string
}

export interface AiRuntimePlatformInfoDto {
    readonly isSupported: boolean
    readonly operatingSystem: string
    readonly architecture: string
    readonly platformName: string
    readonly unsupportedReason: string | null
    readonly compatibilityDescription: string | null
    readonly supportedExtensions: string[]
    readonly supportsOnnxConversion: boolean
}

export interface CheckAiModelFileMd5Input {
    readonly md5: string
}

export interface AiModelFileMd5CheckResultDto {
    readonly md5: string
    readonly exists: boolean
    readonly existingModelId: string | null
    readonly existingModelName: string | null
    readonly existingOriginalFileName: string | null
}

export interface AiModelDto {
    readonly id: string
    readonly name: string
    readonly description: string | null
    readonly modelIdentifiers: AiModelIdentifierDto[]
    readonly version: string | null
    readonly locationKey: string | null
    readonly generationCondition: string | null
    readonly conversionPreference: AiModelConversionPreference
    readonly resolvedConversionType: AiModelResolvedConversionType
    readonly fileCount: number
    readonly files: AiModelFileDto[]
    readonly loadStatus: AiModelLoadStatus
    readonly creatorUserName: string | null
    readonly creationTime: string
    readonly lastModificationTime: string | null
}

export interface AiModelOperationLogDto {
    readonly id: string
    readonly aiModelId: string | null
    readonly modelName: string
    readonly originalFileName: string | null
    readonly operationType: AiModelOperationType
    readonly occurredAt: string
    readonly isSuccess: boolean
    readonly parameterSummary: string | null
    readonly errorMessage: string | null
    readonly durationMs: number
}

export interface GetAiModelListInput {
    readonly filter?: string | null
    readonly startTime?: string | null
    readonly endTime?: string | null
    readonly creatorId?: string | null
    readonly locationKey?: string | null
    readonly generationCondition?: string | null
    readonly conversionPreference?: AiModelConversionPreference | null
    readonly resolvedConversionType?: AiModelResolvedConversionType | null
    readonly fileRole?: AiModelFileRole | null
    readonly fileFormat?: string | null
    readonly loadStatus?: AiModelLoadStatus | null
    readonly sorting?: string | null
    readonly skipCount?: number
    readonly maxResultCount?: number
}

export interface GetAiModelLogListInput {
    readonly aiModelId?: string | null
    readonly filter?: string | null
    readonly operationType?: AiModelOperationType | null
    readonly isFailedOnly?: boolean
    readonly startTime?: string | null
    readonly endTime?: string | null
    readonly skipCount?: number
    readonly maxResultCount?: number
}

export interface UploadAiModelInput {
    readonly name: string
    readonly description?: string | null
    readonly conversionPreference?: AiModelConversionPreference
    readonly resolvedConversionType?: AiModelResolvedConversionType
    readonly identifierNames?: string[]
    readonly locationKey?: string | null
    readonly generationCondition?: string | null
}

export interface AiModelUploadFileInput {
    readonly sessionId: string
    readonly originalFileName: string
    readonly md5: string
    readonly fileRole: AiModelFileRole
    readonly sortOrder: number
}

export interface AiModelFileUpdateInput {
    readonly id: string
    readonly fileRole: AiModelFileRole
    readonly sortOrder: number
}

export interface InitializeAiModelUploadInput {
    readonly originalFileName: string
    readonly fileSizeBytes: number
    readonly chunkSizeBytes: number
    readonly totalChunks: number
    readonly md5: string
    readonly fileRole?: AiModelFileRole
    readonly sortOrder?: number
}

export interface InitializeAiModelUploadResultDto {
    readonly sessionId: string
    readonly originalFileName: string
    readonly fileSizeBytes: number
    readonly chunkSizeBytes: number
    readonly totalChunks: number
    readonly uploadedChunks: number
    readonly uploadedBytes: number
}

export interface AiModelUploadChunkResultDto {
    readonly sessionId: string
    readonly uploadedChunks: number
    readonly totalChunks: number
    readonly uploadedBytes: number
    readonly fileSizeBytes: number
    readonly nextChunkIndex: number
    readonly accepted: boolean
}

export interface AiModelUploadSessionStatusDto {
    readonly sessionId: string
    readonly originalFileName: string
    readonly md5: string
    readonly fileSizeBytes: number
    readonly chunkSizeBytes: number
    readonly totalChunks: number
    readonly uploadedChunks: number
    readonly uploadedBytes: number
    readonly nextChunkIndex: number
    readonly isCompleted: boolean
    readonly lastUpdatedTime: string
}

export interface FindAiModelUploadSessionInput {
    readonly originalFileName: string
    readonly md5: string
    readonly fileSizeBytes: number
    readonly chunkSizeBytes?: number | null
    readonly totalChunks?: number | null
}

export interface CompleteAiModelUploadInput extends UploadAiModelInput {
    readonly files: AiModelUploadFileInput[]
}

export interface UpdateAiModelInput {
    readonly name: string
    readonly description?: string | null
    readonly conversionPreference?: AiModelConversionPreference
    readonly resolvedConversionType?: AiModelResolvedConversionType
    readonly identifierNames?: string[]
    readonly version?: string | null
    readonly locationKey?: string | null
    readonly generationCondition?: string | null
    readonly files?: AiModelFileUpdateInput[]
}

export interface PagedResult<T> {
    readonly items: T[]
    readonly totalCount: number
}

const BASE = '/api/app/ai-model'

export async function getAiRuntimePlatformInfoAsync(): Promise<AiRuntimePlatformInfoDto> {
    const response = await httpClient.get<AiRuntimePlatformInfoDto>(`${BASE}/runtime-platform-info`)
    return response.data
}

export async function getAiModelIdentifierLookupAsync(filter?: string | null): Promise<AiModelIdentifierDto[]> {
    const response = await httpClient.get<AiModelIdentifierDto[]>(`${BASE}/identifier-lookup`, {
        params: { filter: filter || undefined },
    })
    return response.data
}

export async function checkAiModelFileMd5Async(
    input: CheckAiModelFileMd5Input
): Promise<AiModelFileMd5CheckResultDto> {
    const response = await httpClient.post<AiModelFileMd5CheckResultDto>(`${BASE}/check-file-md5`, input)
    return response.data
}

export async function getAiModelListAsync(input: GetAiModelListInput): Promise<PagedResult<AiModelDto>> {
    const response = await httpClient.get<PagedResult<AiModelDto>>(BASE, { params: input })
    return response.data
}

export async function getAiModelAsync(id: string): Promise<AiModelDto> {
    const response = await httpClient.get<AiModelDto>(`${BASE}/${id}`)
    return response.data
}

export async function getAiModelLogsAsync(input: GetAiModelLogListInput): Promise<PagedResult<AiModelOperationLogDto>> {
    const response = await httpClient.get<PagedResult<AiModelOperationLogDto>>(`${BASE}/logs`, { params: input })
    return response.data
}

export async function uploadAiModelAsync(
    file: File,
    input: UploadAiModelInput,
    onProgress?: (percent: number) => void,
    signal?: AbortSignal
): Promise<AiModelDto> {
    const formData = new FormData()
    formData.append('file', file)
    Object.entries(input).forEach(([key, value]) => {
        if (Array.isArray(value)) {
            value.forEach((entry) => {
                if (entry !== undefined && entry !== null && entry !== '') {
                    formData.append(key, String(entry))
                }
            })
            return
        }

        if (value !== undefined && value !== null && value !== '') {
            formData.append(key, String(value))
        }
    })

    const response = await httpClient.post<AiModelDto>(`${BASE}/upload`, formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 0,
        maxBodyLength: Infinity,
        maxContentLength: Infinity,
        onUploadProgress: (event) => {
            if (onProgress && event.total && event.total > 0) {
                onProgress(Math.round((event.loaded * 100) / event.total))
            }
        },
        signal,
    })
    return response.data
}

export async function initializeAiModelUploadAsync(
    input: InitializeAiModelUploadInput
): Promise<InitializeAiModelUploadResultDto> {
    const response = await httpClient.post<InitializeAiModelUploadResultDto>(`${BASE}/initialize-upload`, input)
    return response.data
}

export async function uploadAiModelChunkAsync(
    sessionId: string,
    chunkIndex: number,
    chunk: Blob,
    onProgress?: (loaded: number, total: number) => void,
    signal?: AbortSignal
): Promise<AiModelUploadChunkResultDto> {
    const formData = new FormData()
    formData.append('chunk', chunk, `chunk-${chunkIndex}`)

    const response = await httpClient.post<AiModelUploadChunkResultDto>(`${BASE}/upload-chunk/${sessionId}`, formData, {
        params: { chunkIndex },
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 0,
        maxBodyLength: Infinity,
        maxContentLength: Infinity,
        onUploadProgress: (event) => {
            if (onProgress) {
                onProgress(event.loaded, event.total ?? chunk.size)
            }
        },
        signal,
    })

    return response.data
}

export async function getAiModelUploadSessionStatusAsync(
    sessionId: string
): Promise<AiModelUploadSessionStatusDto> {
    const response = await httpClient.get<AiModelUploadSessionStatusDto>(`${BASE}/upload-session-status/${sessionId}`, {
        timeout: 0,
    })

    return response.data
}

export async function findAiModelUploadSessionAsync(
    input: FindAiModelUploadSessionInput
): Promise<AiModelUploadSessionStatusDto | null> {
    const response = await httpClient.get<AiModelUploadSessionStatusDto | null>(`${BASE}/find-upload-session`, {
        params: input,
        timeout: 0,
    })

    return response.data
}

export async function completeAiModelUploadAsync(
    input: CompleteAiModelUploadInput
): Promise<AiModelDto> {
    const response = await httpClient.post<AiModelDto>(`${BASE}/complete-upload`, input, {
        timeout: 0,
    })
    return response.data
}

export async function abortAiModelUploadAsync(sessionId: string): Promise<void> {
    await httpClient.post(`${BASE}/abort-upload/${sessionId}`, null, {
        timeout: 0,
    })
}

export async function updateAiModelAsync(id: string, input: UpdateAiModelInput): Promise<AiModelDto> {
    const response = await httpClient.put<AiModelDto>(`${BASE}/${id}`, input)
    return response.data
}

export async function deleteAiModelAsync(id: string): Promise<void> {
    await httpClient.delete(`${BASE}/${id}`)
}

export async function startAiModelConversionAsync(id: string): Promise<AiModelConversionStartResultDto> {
    const response = await httpClient.post<AiModelConversionStartResultDto>(`${BASE}/${id}/start-conversion`)
    return response.data
}

export async function deleteAiModelConvertedFileAsync(id: string, fileId: string): Promise<void> {
    await httpClient.delete(`${BASE}/${id}/converted-file`, {
        params: { fileId },
    })
}

export async function loadAiModelAsync(id: string): Promise<AiModelDto> {
    const response = await httpClient.post<AiModelDto>(`${BASE}/${id}/load`)
    return response.data
}

export async function unloadAiModelAsync(id: string): Promise<AiModelDto> {
    const response = await httpClient.post<AiModelDto>(`${BASE}/${id}/unload`)
    return response.data
}

export async function cleanUpOrphanedRecordsAsync(): Promise<number> {
    const response = await httpClient.post<number>(`${BASE}/clean-up-orphaned-records`)
    return response.data
}
