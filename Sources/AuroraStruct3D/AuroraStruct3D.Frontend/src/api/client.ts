/**
 * 全局 axios 客户端：注入 Bearer Token、租户头、统一错误处理
 */
import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { toast } from 'vue-sonner'

import { useAuthStore } from '@/stores/auth'
import { router } from '@/router'
import type { AbpRemoteError } from '@/types/abp'
import { getClientSessionId } from '@/utils/clientSession'

// 生产环境同源；开发环境由 Vite 代理转发到 44315
const baseURL = '/'
const ERROR_TOAST_SHOWN_KEY = '__auroraErrorToastShown'

export const httpClient: AxiosInstance = axios.create({
    baseURL,
    timeout: 30_000,
    withCredentials: false,
})

type ToastMarkedError = {
    message?: string
    [ERROR_TOAST_SHOWN_KEY]?: boolean
}

export function showErrorToastOnce(error: unknown): void {
    const candidate = error as ToastMarkedError
    if (candidate?.[ERROR_TOAST_SHOWN_KEY]) {
        return
    }

    const message = candidate?.message || '未知错误'
    toast.error(message)
    if (candidate && typeof candidate === 'object') {
        candidate[ERROR_TOAST_SHOWN_KEY] = true
    }
}

httpClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
    const auth = useAuthStore()
    if (auth.token) {
        config.headers.set('Authorization', `Bearer ${auth.token}`)
    }
    if (auth.tenantId) {
        // ABP 多租户解析头
        config.headers.set('__tenant', auth.tenantId)
    }
    // 每个标签页独有的设备会话标识（Soft-Exclusive Session ownership key）
    config.headers.set('X-Client-Session-Id', getClientSessionId())
    // 默认接受语言；后续由 i18n Store 同步
    if (!config.headers.has('Accept-Language')) {
        const culture = localStorage.getItem('aurora.culture') || 'zh-CN'
        config.headers.set('Accept-Language', culture)
    }
    return config
})

httpClient.interceptors.response.use(
    (response) => response,
    (error: { response?: { status?: number; data?: unknown }; message?: string }) => {
        const status = error.response?.status
        const data = error.response?.data
        // 仅当响应体为对象时才尝试解析 ABP 错误格式（HTML 或纯文本响应不做解析）
        const remote =
            typeof data === 'object' && data !== null
                ? (data as AbpRemoteError)?.error
                : undefined

        if (status === 401) {
            const auth = useAuthStore()
            auth.reset()
            // 主动跳转到登录页，路由守卫只在导航时触发，无法接管当前页面的 API 401
            router.push({ name: 'Login', query: { redirect: router.currentRoute.value.fullPath } })
        }

        let message: string
        if (remote?.message) {
            message = remote.message
        } else {
            // 对非 ABP 格式响应（如 HTML 错误页、纯文本等）给出友好中文提示
            switch (status) {
                case 400: message = '请求参数错误 (400)'; break
                case 401: message = '登录已过期，请重新登录 (401)'; break
                case 403: message = '无权限执行此操作 (403)'; break
                case 404: message = '请求的资源不存在 (404)'; break
                case 500: message = '服务器内部错误，请稍后重试 (500)'; break
                case 502: message = '网关错误，请稍后重试 (502)'; break
                case 503: message = '服务不可用，请稍后重试 (503)'; break
                default: message = error.message || '未知错误'; break
            }
        }

        if (typeof error === 'object' && error !== null) {
            ; (error as ToastMarkedError).message = message
                ; (error as ToastMarkedError)[ERROR_TOAST_SHOWN_KEY] = true
        }
        toast.error(message)
        return Promise.reject(error)
    }
)
