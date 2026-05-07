/**
 * Swagger API 客户端封装
 * - 获取 OpenAPI 3.0 文档
 * - 解析并分组接口
 * - 执行在线调试请求
 */
import axios from 'axios'
import { useAuthStore } from '@/stores/auth'
import type {
  SwaggerDocument,
  ApiGroup,
  ApiEndpoint,
  HttpMethod,
  DebugRequest,
  DebugResponse,
  SwaggerSchema,
} from '@/types/swagger'

/** Swagger JSON 文档地址 */
const SWAGGER_JSON_URL = '/swagger/AbpPro/swagger.json'

/** 获取 Swagger 文档 */
export async function fetchSwaggerDocument(): Promise<SwaggerDocument> {
  const { data } = await axios.get<SwaggerDocument>(SWAGGER_JSON_URL)
  return data
}

/** 解析 $ref 引用，返回对应的 Schema */
export function resolveRef(doc: SwaggerDocument, ref: string): SwaggerSchema | undefined {
  if (!ref.startsWith('#/')) return undefined
  const parts = ref.slice(2).split('/')
  let cur: unknown = doc
  for (const p of parts) {
    if (cur && typeof cur === 'object') {
      cur = (cur as Record<string, unknown>)[p]
    } else {
      return undefined
    }
  }
  return cur as SwaggerSchema
}

/** 按 Tag 分组接口列表 */
export function groupEndpointsByTag(doc: SwaggerDocument): ApiGroup[] {
  const map = new Map<string, ApiEndpoint[]>()
  for (const [path, methods] of Object.entries(doc.paths ?? {})) {
    for (const [method, operation] of Object.entries(methods)) {
      const httpMethod = method.toUpperCase() as HttpMethod
      const tags = operation.tags?.length ? operation.tags : ['默认']
      for (const tag of tags) {
        if (!map.has(tag)) map.set(tag, [])
        map.get(tag)!.push({ method: httpMethod, path, operation })
      }
    }
  }
  return Array.from(map.entries()).map(([tag, endpoints]) => ({ tag, endpoints }))
}

/** 根据 Schema 生成示例请求体字符串 */
export function generateExampleBody(doc: SwaggerDocument, schema?: SwaggerSchema): string {
  if (!schema) return ''
  const resolved = schema.$ref ? resolveRef(doc, schema.$ref) : schema
  if (!resolved) return ''
  const example = buildExample(doc, resolved, 0)
  return JSON.stringify(example, null, 2)
}

function buildExample(doc: SwaggerDocument, schema: SwaggerSchema, depth: number): unknown {
  if (depth > 4) return null
  if (schema.$ref) {
    const r = resolveRef(doc, schema.$ref)
    return r ? buildExample(doc, r, depth + 1) : null
  }
  if (schema.example !== undefined) return schema.example
  if (schema.default !== undefined) return schema.default
  if (schema.enum?.length) return schema.enum[0]
  switch (schema.type) {
    case 'object': {
      const obj: Record<string, unknown> = {}
      for (const [k, v] of Object.entries(schema.properties ?? {})) {
        obj[k] = buildExample(doc, v, depth + 1)
      }
      return obj
    }
    case 'array':
      return schema.items ? [buildExample(doc, schema.items, depth + 1)] : []
    case 'integer':
    case 'number':
      return 0
    case 'boolean':
      return false
    case 'string':
      return schema.format === 'date-time' ? new Date().toISOString() : ''
    default:
      return null
  }
}

/** 执行调试请求 */
export async function executeDebugRequest(
  endpoint: ApiEndpoint,
  req: DebugRequest,
): Promise<DebugResponse> {
  const authStore = useAuthStore()
  let url = endpoint.path
  for (const [k, v] of Object.entries(req.pathParams)) {
    url = url.replace(`{${k}}`, encodeURIComponent(v))
  }
  const params: Record<string, string> = {}
  for (const [k, v] of Object.entries(req.queryParams)) {
    if (v !== '') params[k] = v
  }
  const headers: Record<string, string> = { ...req.headers }
  if (authStore.token) headers['Authorization'] = `Bearer ${authStore.token}`
  if (authStore.tenantId) headers['__tenant'] = authStore.tenantId
  const method = endpoint.method.toLowerCase()
  const hasBody = ['post', 'put', 'patch'].includes(method)
  if (hasBody && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json'
  }
  const start = Date.now()
  try {
    const resp = await axios.request({
      url,
      method,
      params,
      headers,
      data: hasBody && req.body ? req.body : undefined,
      validateStatus: () => true,
    })
    const duration = Date.now() - start
    const respHeaders: Record<string, string> = {}
    for (const [k, v] of Object.entries(resp.headers ?? {})) {
      if (typeof v === 'string') respHeaders[k] = v
    }
    let body = ''
    if (typeof resp.data === 'string') {
      body = resp.data
    } else {
      body = JSON.stringify(resp.data, null, 2)
    }
    return { status: resp.status, statusText: resp.statusText, headers: respHeaders, body, duration }
  } catch (e: unknown) {
    const duration = Date.now() - start
    const msg = e instanceof Error ? e.message : String(e)
    return { status: 0, statusText: '请求失败', headers: {}, body: msg, duration }
  }
}

/** 返回 HTTP 方法对应的颜色样式类 */
export function methodColor(method: string): string {
  switch (method.toUpperCase()) {
    case 'GET':    return 'bg-green-500/20 text-green-400 border-green-500/30'
    case 'POST':   return 'bg-blue-500/20 text-blue-400 border-blue-500/30'
    case 'PUT':    return 'bg-yellow-500/20 text-yellow-400 border-yellow-500/30'
    case 'DELETE': return 'bg-red-500/20 text-red-400 border-red-500/30'
    case 'PATCH':  return 'bg-purple-500/20 text-purple-400 border-purple-500/30'
    default:       return 'bg-muted text-muted-foreground'
  }
}

/** 返回 HTTP 状态码对应的颜色样式类 */
export function statusColor(status: number): string {
  if (status >= 200 && status < 300) return 'text-green-400'
  if (status >= 300 && status < 400) return 'text-yellow-400'
  if (status >= 400 && status < 500) return 'text-orange-400'
  if (status >= 500) return 'text-red-400'
  return 'text-muted-foreground'
}