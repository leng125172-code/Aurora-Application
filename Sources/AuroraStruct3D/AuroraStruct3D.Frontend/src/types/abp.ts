/**
 * ABP 通用响应/请求类型定义
 */

/** ABP 远程错误响应（/api/* 失败时通常返回此结构） */
export interface AbpRemoteError {
    readonly error: {
        readonly code?: string
        readonly message: string
        readonly details?: string
        readonly validationErrors?: readonly {
            readonly message: string
            readonly members?: readonly string[]
        }[]
    }
}

/** ABP 应用配置（/api/abp/application-configuration） */
export interface AbpApplicationConfiguration {
    readonly currentUser: {
        readonly isAuthenticated: boolean
        readonly id?: string
        readonly tenantId?: string | null
        readonly userName?: string
        readonly email?: string
        readonly roles?: readonly string[]
    }
    readonly currentTenant: {
        readonly id?: string | null
        readonly name?: string | null
        readonly isAvailable: boolean
    }
    readonly auth: {
        readonly grantedPolicies: Readonly<Record<string, boolean>>
    }
    readonly localization: {
        readonly currentCulture: { readonly cultureName: string; readonly name: string }
        readonly languages: readonly {
            readonly cultureName: string
            readonly uiCultureName: string
            readonly displayName: string
            readonly flagIcon?: string
        }[]
    }
}

/** OAuth2 password grant 令牌响应 */
export interface AbpTokenResponse {
    readonly access_token: string
    readonly expires_in: number
    readonly token_type: string
    readonly refresh_token?: string
    readonly scope?: string
}
