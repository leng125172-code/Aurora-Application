/** HTTP 请求方法枚举 */
export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH' | 'HEAD' | 'OPTIONS'

/** OpenAPI 3.0 Schema 定义 */
export interface SwaggerSchema {
  type?: string
  format?: string
  description?: string
  example?: unknown
  enum?: unknown[]
  items?: SwaggerSchema
  properties?: Record<string, SwaggerSchema>
  required?: string[]
  $ref?: string
  allOf?: SwaggerSchema[]
  oneOf?: SwaggerSchema[]
  anyOf?: SwaggerSchema[]
  nullable?: boolean
  default?: unknown
  minimum?: number
  maximum?: number
  minLength?: number
  maxLength?: number
  pattern?: string
}

/** OpenAPI 参数定义 */
export interface SwaggerParameter {
  name: string
  in: 'query' | 'path' | 'header' | 'cookie'
  description?: string
  required?: boolean
  schema?: SwaggerSchema
  example?: unknown
}

/** OpenAPI 请求体 */
export interface SwaggerRequestBody {
  description?: string
  required?: boolean
  content?: Record<string, { schema?: SwaggerSchema }>
}

/** OpenAPI 响应定义 */
export interface SwaggerResponse {
  description?: string
  content?: Record<string, { schema?: SwaggerSchema }>
}

/** OpenAPI 单个接口操作 */
export interface SwaggerOperation {
  operationId?: string
  summary?: string
  description?: string
  tags?: string[]
  parameters?: SwaggerParameter[]
  requestBody?: SwaggerRequestBody
  responses?: Record<string, SwaggerResponse>
  deprecated?: boolean
  security?: Record<string, string[]>[]
}

/** OpenAPI 文档根结构 */
export interface SwaggerDocument {
  openapi: string
  info: {
    title: string
    version: string
    description?: string
  }
  paths: Record<string, Record<string, SwaggerOperation>>
  components?: {
    schemas?: Record<string, SwaggerSchema>
    securitySchemes?: Record<string, unknown>
  }
  tags?: Array<{ name: string; description?: string }>
}

/** 分组后的 API 端点 */
export interface ApiEndpoint {
  method: HttpMethod
  path: string
  operation: SwaggerOperation
}

/** 按 Tag 分组的接口组 */
export interface ApiGroup {
  tag: string
  endpoints: ApiEndpoint[]
}

/** 调试请求参数 */
export interface DebugRequest {
  pathParams: Record<string, string>
  queryParams: Record<string, string>
  headers: Record<string, string>
  body: string
}

/** 调试响应结果 */
export interface DebugResponse {
  status: number
  statusText: string
  headers: Record<string, string>
  body: string
  duration: number
}