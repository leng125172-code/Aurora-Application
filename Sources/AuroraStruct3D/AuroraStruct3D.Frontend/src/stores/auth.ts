/**
 * 认证状态管理：存储 ABP 颁发的 token、当前用户、租户信息
 * 注意：实际登录/登出 API 调用放在 @/api/auth.ts；本 Store 只负责状态
 */
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

const TOKEN_KEY = 'aurora.token'
const TENANT_KEY = 'aurora.tenantId'

export interface CurrentUser {
    readonly id: string
    readonly userName: string
    readonly email?: string
    readonly tenantId?: string | null
    readonly roles: readonly string[]
}

export const useAuthStore = defineStore('auth', () => {
    // 令牌（从 localStorage 恢复，便于刷新页面保持登录）
    const token = ref<string | null>(localStorage.getItem(TOKEN_KEY))
    const tenantId = ref<string | null>(localStorage.getItem(TENANT_KEY))
    const currentUser = ref<CurrentUser | null>(null)

    /**
     * 解析 JWT payload 中的过期时间（exp 为 Unix 秒级时间戳）
     * 解析失败时返回 null，视为已过期
     */
    function getTokenExpiry(jwt: string): number | null {
        try {
            const payload = jwt.split('.')[1]
            const decoded = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')))
            return typeof decoded.exp === 'number' ? decoded.exp : null
        } catch {
            return null
        }
    }

    /** token 存在且未过期才视为已认证 */
    const isAuthenticated = computed<boolean>(() => {
        if (!token.value) return false
        const exp = getTokenExpiry(token.value)
        // exp 为 null 说明解析失败，当作过期处理
        if (exp === null) return false
        // exp 是秒级时间戳，Date.now() 是毫秒
        return Date.now() < exp * 1000
    })

    /** 设置令牌并持久化 */
    function setToken(value: string | null): void {
        token.value = value
        if (value) {
            localStorage.setItem(TOKEN_KEY, value)
        } else {
            localStorage.removeItem(TOKEN_KEY)
        }
    }

    /** 设置租户 Id 并持久化 */
    function setTenantId(value: string | null): void {
        tenantId.value = value
        if (value) {
            localStorage.setItem(TENANT_KEY, value)
        } else {
            localStorage.removeItem(TENANT_KEY)
        }
    }

    /** 设置当前用户信息 */
    function setCurrentUser(user: CurrentUser | null): void {
        currentUser.value = user
    }

    /** 清空所有认证信息（用于登出） */
    function reset(): void {
        setToken(null)
        setCurrentUser(null)
        // 租户 Id 保留，便于下次登录预填
    }

    return {
        token,
        tenantId,
        currentUser,
        isAuthenticated,
        setToken,
        setTenantId,
        setCurrentUser,
        reset,
    }
})
