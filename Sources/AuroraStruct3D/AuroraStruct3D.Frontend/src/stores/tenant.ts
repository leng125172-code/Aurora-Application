/**
 * 当前租户信息（从 ABP 应用配置接口同步）
 * 与 auth store 中的 tenantId 解耦：tenantId 用于请求头，tenant 用于展示
 */
import { defineStore } from 'pinia'
import { ref } from 'vue'

export interface CurrentTenantInfo {
    readonly id: string | null
    readonly name: string | null
    readonly isAvailable: boolean
}

export const useTenantStore = defineStore('tenant', () => {
    const current = ref<CurrentTenantInfo | null>(null)

    function setCurrent(value: CurrentTenantInfo | null): void {
        current.value = value
    }

    return { current, setCurrent }
})
