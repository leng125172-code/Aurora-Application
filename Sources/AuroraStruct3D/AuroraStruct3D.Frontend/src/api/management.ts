/**
 * 系统管理相关 API
 * 端点来自 Lion.AbpPro.BasicManagement，路径基于 ABP Pro 自定义路由
 */
import { httpClient } from '@/api/client'

// ===================== 公共类型 =====================

export interface PageInput {
    readonly pageIndex?: number
    readonly pageSize?: number
    readonly filter?: string | null
    readonly sorting?: string | null
}

export interface PagedResult<T> {
    readonly items?: T[] | null
    readonly totalCount?: number
}

// ===================== 用户管理 =====================

export interface UserDto {
    readonly id?: string
    readonly userName?: string | null
    readonly name?: string | null
    readonly surname?: string | null
    readonly email?: string | null
    readonly phoneNumber?: string | null
    readonly isActive?: boolean
    readonly twoFactorEnabled?: boolean
    readonly creationTime?: string
    readonly lockoutEnabled?: boolean
    readonly lockoutEnd?: string | null
    readonly concurrencyStamp?: string | null
}

export interface CreateUserInput {
    readonly userName: string
    readonly name?: string | null
    readonly surname?: string | null
    readonly email: string
    readonly phoneNumber?: string | null
    readonly isActive?: boolean
    readonly lockoutEnabled?: boolean
    readonly roleNames?: string[] | null
    readonly password: string
}

export interface UpdateUserInput {
    readonly id: string
    readonly userName: string
    readonly name?: string | null
    readonly surname?: string | null
    readonly email: string
    readonly phoneNumber?: string | null
    readonly isActive: boolean
    readonly lockoutEnabled?: boolean
    readonly roleNames?: string[] | null
    readonly concurrencyStamp?: string | null
}

export interface ResetPasswordInput {
    readonly id: string
    readonly password: string
}

export interface SetUserActiveInput {
    readonly id: string
    readonly isActive: boolean
}

export interface RoleAssignInput {
    readonly id: string
}

/** 分页获取用户列表 */
export async function getUserPageAsync(input: PageInput): Promise<PagedResult<UserDto>> {
    const response = await httpClient.post<PagedResult<UserDto>>('/Users/page', input)
    return response.data
}

/** 创建用户 */
export async function createUserAsync(input: CreateUserInput): Promise<UserDto> {
    const response = await httpClient.post<UserDto>('/Users/create', {
        ...input,
        isActive: input.isActive ?? true,
        lockoutEnabled: input.lockoutEnabled ?? true,
    })
    return response.data
}

/** 更新用户 */
export async function updateUserAsync(input: UpdateUserInput): Promise<UserDto> {
    const { id, ...userInfo } = input
    const response = await httpClient.post<UserDto>('/Users/update', {
        userId: id,
        userInfo,
    })
    return response.data
}

/** 删除用户 */
export async function deleteUserAsync(id: string): Promise<void> {
    await httpClient.post('/Users/delete', { id })
}

/** 重置密码 */
export async function resetUserPasswordAsync(input: ResetPasswordInput): Promise<void> {
    await httpClient.post('/Users/resetPassword', {
        userId: input.id,
        password: input.password,
    })
}

/** 启用或禁用用户。 */
export async function setUserActiveAsync(input: SetUserActiveInput): Promise<void> {
    await httpClient.post('/Users/setActive', {
        userId: input.id,
        isActive: input.isActive,
    })
}

/** 获取用户角色 */
export async function getUserRolesAsync(id: string): Promise<string[]> {
    const response = await httpClient.post<{ items?: { name?: string | null }[] | null }>('/Users/role', { id })
    return (response.data.items ?? []).map((r) => r.name ?? '').filter(Boolean)
}

/** 覆盖更新用户所属角色。传入空数组可移除全部非静态角色。 */
export async function updateUserRolesAsync(id: string, roleNames: readonly string[]): Promise<void> {
    await httpClient.post('/Users/roles/update', {
        userId: id,
        roleNames: [...roleNames],
    })
}

// ===================== 角色管理 =====================

export interface RoleDto {
    readonly id?: string
    readonly name?: string | null
    readonly isDefault?: boolean
    readonly isStatic?: boolean
    readonly isPublic?: boolean
    readonly creationTime?: string
    readonly concurrencyStamp?: string | null
}

export interface CreateRoleInput {
    readonly name: string
    readonly isDefault?: boolean
    readonly isPublic?: boolean
}

export interface UpdateRoleInput extends CreateRoleInput {
    readonly id: string
    readonly concurrencyStamp?: string | null
}

/** 分页获取角色列表 */
export async function getRolePageAsync(input: PageInput): Promise<PagedResult<RoleDto>> {
    const response = await httpClient.post<PagedResult<RoleDto>>('/Roles/page', input)
    return response.data
}

/** 获取所有角色（下拉用） */
export async function getAllRolesAsync(): Promise<RoleDto[]> {
    const response = await httpClient.post<{ items?: RoleDto[] | null }>('/Roles/all', {})
    return response.data.items ?? []
}

/** 创建角色 */
export async function createRoleAsync(input: CreateRoleInput): Promise<RoleDto> {
    const response = await httpClient.post<RoleDto>('/Roles/create', input)
    return response.data
}

/** 更新角色 */
export async function updateRoleAsync(input: UpdateRoleInput): Promise<RoleDto> {
    const { id, ...roleInfo } = input
    const response = await httpClient.post<RoleDto>('/Roles/update', {
        roleId: id,
        roleInfo,
    })
    return response.data
}

/** 删除角色 */
export async function deleteRoleAsync(id: string): Promise<void> {
    await httpClient.post('/Roles/delete', { id })
}

// ===================== 权限管理 =====================

/** ABP 权限授予提供者名称。 */
export const PermissionProvider = {
    Role: 'R',
    User: 'U',
} as const

export type PermissionProviderName = (typeof PermissionProvider)[keyof typeof PermissionProvider]

export interface PermissionTreeNode {
    readonly key: string
    readonly title: string
    readonly children: PermissionTreeNode[]
}

export interface PermissionResult {
    readonly grants: string[]
    readonly allGrants: string[]
    readonly permissions: PermissionTreeNode[]
}

export interface GetPermissionsInput {
    readonly providerName: PermissionProviderName
    readonly providerKey: string
}

export interface PermissionGrantInput {
    readonly name: string
    readonly isGranted: boolean
}

export interface UpdatePermissionsInput extends GetPermissionsInput {
    readonly permissions: PermissionGrantInput[]
}

/** 获取指定角色或用户的权限树与授权状态。 */
export async function getPermissionsAsync(input: GetPermissionsInput): Promise<PermissionResult> {
    const response = await httpClient.post<PermissionResult>('/Permissions/tree', input)
    return response.data
}

/** 覆盖保存指定角色或用户的权限授予状态。 */
export async function updatePermissionsAsync(input: UpdatePermissionsInput): Promise<void> {
    await httpClient.post('/Permissions/update', {
        providerName: input.providerName,
        providerKey: input.providerKey,
        updatePermissionsDto: {
            permissions: input.permissions,
        },
    })
}

// ===================== 租户管理 =====================

export interface TenantDto {
    readonly id?: string
    readonly name?: string | null
    readonly concurrencyStamp?: string | null
}

export interface CreateTenantInput {
    readonly name: string
    readonly adminEmailAddress: string
    readonly adminPassword: string
}

export interface UpdateTenantInput {
    readonly id: string
    readonly name: string
    readonly concurrencyStamp?: string | null
}

/** 分页获取租户列表 */
export async function getTenantPageAsync(input: PageInput): Promise<PagedResult<TenantDto>> {
    const response = await httpClient.post<PagedResult<TenantDto>>('/Tenants/page', input)
    return response.data
}

/** 创建租户 */
export async function createTenantAsync(input: CreateTenantInput): Promise<TenantDto> {
    const response = await httpClient.post<TenantDto>('/Tenants/create', input)
    return response.data
}

/** 更新租户 */
export async function updateTenantAsync(input: UpdateTenantInput): Promise<TenantDto> {
    const response = await httpClient.post<TenantDto>('/Tenants/update', input)
    return response.data
}

/** 删除租户 */
export async function deleteTenantAsync(id: string): Promise<void> {
    await httpClient.post('/Tenants/delete', { id })
}
