using Volo.Abp.PermissionManagement;

namespace Lion.AbpPro.BasicManagement.Roles
{
    [Authorize]
    public class RolePermissionAppService : BasicManagementAppService, IRolePermissionAppService
    {
        private readonly IPermissionAppService _rolePermissionAppService;
        private readonly PermissionOptions _permissionOptions;
        private readonly AbpMultiTenancyOptions _multiTenancyOptions;
        private const string PermissionPrefix = "AbpTenantManagement";

        public RolePermissionAppService(
            IPermissionAppService rolePermissionAppService,
            IOptions<PermissionOptions> permissionOptions,
            IOptions<AbpMultiTenancyOptions> multiTenancyOptions
        )
        {
            _rolePermissionAppService = rolePermissionAppService;
            _multiTenancyOptions = multiTenancyOptions.Value;
            _permissionOptions = permissionOptions.Value;
        }

        /// <summary>
        /// 获取所有权限
        /// </summary>
        public virtual async Task<PermissionOutput> GetPermissionAsync(GetPermissionInput input)
        {
            await CheckManagePermissionAsync(input.ProviderName);
            var permissions = await _rolePermissionAppService.GetAsync(
                input.ProviderName,
                input.ProviderKey
            );

            return BuildTreeData(permissions.Groups);
        }

        /// <summary>
        /// 更新权限
        /// </summary>
        public virtual async Task UpdatePermissionAsync(UpdateRolePermissionsInput input)
        {
            await CheckManagePermissionAsync(input.ProviderName);
            await _rolePermissionAppService.UpdateAsync(
                input.ProviderName,
                input.ProviderKey,
                input.UpdatePermissionsDto
            );
        }

        private Task CheckManagePermissionAsync(string providerName)
        {
            var permissionName = providerName == "U"
                ? IdentityPermissions.Users.ManagePermissions
                : IdentityPermissions.Roles.ManagePermissions;
            return AuthorizationService.CheckAsync(permissionName);
        }

        /// <summary>
        /// 生成权限树
        /// </summary>
        private PermissionOutput BuildTreeData(List<PermissionGroupDto> input)
        {
            var result = new PermissionOutput();

            if (!_multiTenancyOptions.IsEnabled)
            {
                input = input.Where(e => !e.Name.StartsWith(PermissionPrefix)).ToList();
            }

            var permissions = new List<PermissionTreeDto>();

            foreach (var group in input)
            {
                if (_permissionOptions.IsExclude(group.Name))
                    continue;

                // 获取分组信息
                var groupPermission = new PermissionTreeDto
                {
                    Key = group.Name,
                    Title =
                        group.Name == "AbpIdentity"
                            ? L[$"Permission:SystemManagement"]
                            : group.DisplayName,
                };
                if (
                    group.Permissions.Any(p => p.IsGranted == true && p.Name.StartsWith(group.Name))
                )
                {
                    result.Grants.Add(group.Name);
                }

                // 获取所有已授权和未授权权限集合
                foreach (var item in group.Permissions)
                {
                    if (_permissionOptions.IsExclude(item.Name))
                        continue;
                    result.AllGrants.Add(item.Name);
                    if (item.IsGranted)
                    {
                        result.Grants.Add(item.Name);
                    }
                }

                // 递归菜单
                var childTreeMenu = RecursionMenu(group.Permissions, null);

                groupPermission.Children.AddRange(childTreeMenu.Children);

                permissions.Add(groupPermission);
            }

            result.Permissions = permissions;
            return result;
        }

        /// <summary>
        /// 递归菜单
        /// </summary>
        private PermissionTreeDto RecursionMenu(
            List<PermissionGrantInfoDto> permissionGrantInfoDtos,
            string parentName
        )
        {
            var tree = new PermissionTreeDto();
            var permissions = permissionGrantInfoDtos
                .Where(e => e.ParentName == parentName && !_permissionOptions.IsExclude(e.Name))
                .ToList();
            foreach (var item in permissions)
            {
                var child = new PermissionTreeDto { Key = item.Name, Title = item.DisplayName };
                child.Children.AddRange(RecursionMenu(permissionGrantInfoDtos, item.Name).Children);
                tree.Children.Add(child);
            }

            return tree;
        }
    }
}
