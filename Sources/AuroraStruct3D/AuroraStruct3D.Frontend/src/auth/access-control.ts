export const AppPermissions = {
    operation: 'AuroraStruct3D.Access.Operation',
    management: 'AuroraStruct3D.Access.Management',
} as const

export const AccessLevel = {
    guest: 'guest',
    restricted: 'restricted',
    operator: 'operator',
    administrator: 'administrator',
} as const

export type AccessLevel = (typeof AccessLevel)[keyof typeof AccessLevel]
export type RequiredAccess = typeof AccessLevel.operator | typeof AccessLevel.administrator

const ADMIN_ROLE_NAMES = new Set(['admin', 'administrator'])
const OPERATOR_ROLE_NAMES = new Set(['operator'])

export function resolveAccessLevel(
    isAuthenticated: boolean,
    roles: readonly string[],
    grantedPolicies: Readonly<Record<string, boolean>>,
    authorizationLoaded: boolean
): AccessLevel {
    if (!isAuthenticated) return AccessLevel.guest

    if (grantedPolicies[AppPermissions.management] === true) {
        return AccessLevel.administrator
    }
    if (grantedPolicies[AppPermissions.operation] === true) {
        return AccessLevel.operator
    }

    // 权限配置加载成功后以服务端策略为准，角色名只作为配置暂不可用时的降级判断。
    if (!authorizationLoaded) {
        const normalizedRoles = roles.map((role) => role.toLowerCase())
        if (normalizedRoles.some((role) => ADMIN_ROLE_NAMES.has(role))) {
            return AccessLevel.administrator
        }
        if (normalizedRoles.some((role) => OPERATOR_ROLE_NAMES.has(role))) {
            return AccessLevel.operator
        }
    }

    // 已登录但未分配应用访问权限，不能退化为操作员。
    return AccessLevel.restricted
}

export function satisfiesAccess(level: AccessLevel, required: RequiredAccess): boolean {
    if (required === AccessLevel.administrator) return level === AccessLevel.administrator
    return level === AccessLevel.operator || level === AccessLevel.administrator
}
