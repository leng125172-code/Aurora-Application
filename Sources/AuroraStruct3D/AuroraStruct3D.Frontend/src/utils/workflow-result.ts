export type WorkflowResultPresentation =
    | 'boolean'
    | 'number'
    | 'object'
    | 'array'
    | 'image'
    | 'point-cloud'
    | 'model-3d'
    | 'cad'
    | 'json-file'
    | 'text-file'
    | 'file'
    | 'text'
    | 'unsupported-binary'
    | 'empty'

const IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'bmp', 'webp', 'tif', 'tiff'])
const POINT_CLOUD_EXTENSIONS = new Set(['ply', 'pcd', 'xyz', 'pts', 'asc', 'obj'])
const MODEL_3D_EXTENSIONS = new Set(['stl', 'glb', 'gltf'])
const CAD_EXTENSIONS = new Set(['step', 'stp', 'iges', 'igs'])
const TEXT_EXTENSIONS = new Set(['txt', 'csv', 'log'])
const NUMERIC_TYPES = new Set(['int', 'long', 'float', 'double', 'decimal'])
const RAW_BINARY_TYPES = new Set(['mat', 'pointclouddata'])

export function workflowResultBlobKey(value: unknown): string {
    if (typeof value !== 'string') return ''
    const normalized = value.replace(/^"+|"+$/g, '')
    if (!normalized.includes('/api/app/operator-file/')) return normalized
    try {
        return new URL(normalized, 'http://workflow.local').searchParams.get('blobName') ?? normalized
    } catch {
        return normalized
    }
}

export function workflowResultFileExtension(value: unknown): string {
    const normalized = workflowResultBlobKey(value).split(/[?#]/, 1)[0]?.replaceAll('\\', '/') ?? ''
    const fileName = normalized.slice(normalized.lastIndexOf('/') + 1)
    const match = /\.([a-z][a-z0-9]{0,9})$/i.exec(fileName)
    return match?.[1]?.toLowerCase() ?? ''
}

export function workflowResultFileName(value: unknown): string {
    const normalized = workflowResultBlobKey(value).split(/[?#]/, 1)[0]?.replaceAll('\\', '/') ?? ''
    return normalized.slice(normalized.lastIndexOf('/') + 1)
}

export function classifyWorkflowResult(
    valueType: string | undefined,
    value: unknown,
): WorkflowResultPresentation {
    if (value === null || value === undefined) {
        return RAW_BINARY_TYPES.has((valueType ?? '').toLowerCase())
            ? 'unsupported-binary'
            : 'empty'
    }

    const type = (valueType ?? '').toLowerCase()
    if (type === 'bool' || typeof value === 'boolean') return 'boolean'
    if (NUMERIC_TYPES.has(type) || typeof value === 'number') return 'number'
    if (type === 'array' || Array.isArray(value)) return 'array'
    if (type === 'object' || typeof value === 'object') return 'object'
    if (RAW_BINARY_TYPES.has(type)) return 'unsupported-binary'
    const isFileReference = type === 'blob'
        || (typeof value === 'string' && value.includes('/api/app/operator-file/'))
    if (!isFileReference) return 'text'

    const extension = workflowResultFileExtension(value)
    if (!extension) return 'file'
    if (IMAGE_EXTENSIONS.has(extension)) return 'image'
    if (POINT_CLOUD_EXTENSIONS.has(extension)) return 'point-cloud'
    if (MODEL_3D_EXTENSIONS.has(extension)) return 'model-3d'
    if (CAD_EXTENSIONS.has(extension)) return 'cad'
    if (extension === 'json') return 'json-file'
    if (TEXT_EXTENSIONS.has(extension)) return 'text-file'
    return 'file'
}

export function isWorkflowInspectionResult(value: unknown): boolean {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false
    const record = value as Record<string, unknown>
    const resultCode = typeof record.resultCode === 'string' ? record.resultCode.toUpperCase() : ''
    return ['OK', 'NG', 'UNKNOWN', 'ERROR'].includes(resultCode)
        && typeof record.isValid === 'boolean'
        && typeof record.isOk === 'boolean'
}

export function workflowBlobDownloadUrl(blobKey: string): string {
    return `/api/app/operator-file/download?blobName=${encodeURIComponent(blobKey)}`
}
