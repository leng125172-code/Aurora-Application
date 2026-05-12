/**
 * 认证相关 API
 *
 * 后端实际暴露的是 Lion.AbpPro.BasicManagement 中的 IAccountAppService（自定义 JWT），
 * 而非 Volo OpenIddict 的 /connect/token。对应 HTTP 路由：
 *   - POST /api/app/account/login                用户名密码登录
 *   - POST /api/app/account/refresh-token        刷新 token
 */
import { httpClient } from '@/api/client'

export interface LoginPayload {
    readonly username: string
    readonly password: string
    readonly tenantId?: string | null
}

/** 后端 LoginOutput（见 Lion.AbpPro.BasicManagement.Users.Dtos.LoginOutput） */
export interface LoginResult {
    readonly id: string
    readonly name: string
    readonly userName: string
    readonly token: string
    readonly refreshToken: string
    readonly roles: string[]
}

/** 用户名密码登录 */
export async function loginAsync(payload: LoginPayload): Promise<LoginResult> {
    const response = await httpClient.post<LoginResult>(
        '/api/app/account/login',
        {
            name: payload.username,
            password: payload.password,
        },
        {
            headers: {
                ...(payload.tenantId ? { __tenant: payload.tenantId } : {}),
            },
        }
    )
    return response.data
}

/** 登出：清除本地 token 即可（自定义 JWT 无服务端 session） */
export async function logoutAsync(): Promise<void> {
    // 当前后端未提供登出端点；如需让 refreshToken 失效，可在此扩展
    return Promise.resolve()
}
