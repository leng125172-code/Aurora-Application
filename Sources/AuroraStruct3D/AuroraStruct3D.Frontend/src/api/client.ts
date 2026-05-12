/**
 * 全局 axios 客户端：注入 Bearer Token、租户头、统一错误处理
 */
import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { toast } from 'vue-sonner'

import { useAuthStore } from '@/stores/auth'
import { router } from '@/router'
import type { AbpRemoteError } from '@/types/abp'

// 生产环境同源；开发环境由 Vite 代理转发到 44315
const baseURL = '/'

export const httpClient: AxiosInstance = axios.create({
    baseURL,
    timeout: 30_000,
    withCredentials: false,
})

httpClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
    const auth = useAuthStore()
    if (auth.token) {
        config.headers.set('Authorization', `Bearer ${auth.token}`)
    }
    if (auth.tenantId) {
        // ABP 多租户解析头
        config.headers.set('__tenant', auth.tenantId)
    }
    // 默认接受语言；后续由 i18n Store 同步
    if (!config.headers.has('Accept-Language')) {
        const culture = localStorage.getItem('aurora.culture') || 'zh-CN'
        config.headers.set('Accept-Language', culture)
    }
    return config
})

httpClient.interceptors.response.use(
    (response) => response,
    (error: { response?: { status?: number; data?: AbpRemoteError }; message?: string }) => {
        const status = error.response?.status
        const remote = error.response?.data?.error

        if (status === 401) {
            const auth = useAuthStore()
            auth.reset()
            // 主动跳转到登录页，路由守卫只在导航时触发，无法接管当前页面的 API 401
            router.push({ name: 'Login', query: { redirect: router.currentRoute.value.fullPath } })
        }

        const message = remote?.message || error.message || '未知错误'
        toast.error(message)

        return Promise.reject(error)
    }
)
