/**
 * 拉取 ABP 应用配置（当前用户、租户、本地化、权限等）
 * 端点：GET /api/abp/application-configuration
 */
import { httpClient } from '@/api/client'
import type { AbpApplicationConfiguration } from '@/types/abp'

export async function getApplicationConfigurationAsync(): Promise<AbpApplicationConfiguration> {
    const response = await httpClient.get<AbpApplicationConfiguration>('/api/abp/application-configuration')
    return response.data
}
