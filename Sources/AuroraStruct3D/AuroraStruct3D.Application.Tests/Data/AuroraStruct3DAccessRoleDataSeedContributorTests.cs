using AuroraStruct3D.Data;
using Xunit;

namespace AuroraStruct3D.Data;

public class AuroraStruct3DAccessRoleDataSeedContributorTests
{
    [Fact]
    public void Fixed_roles_should_have_unique_names_and_viewer_should_be_the_only_default()
    {
        FixedRoleDefinition[] roles =
            AuroraStruct3DAccessRoleDataSeedContributor.FixedRoles.ToArray();

        Assert.Equal(4, roles.Length);
        Assert.Equal(4, roles.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["admin", "manager", "operator", "viewer"],
            roles.Select(x => x.Name).ToArray()
        );
        Assert.Equal(
            AuroraStruct3DAccessRoleDataSeedContributor.ViewerRoleName,
            Assert.Single(roles, x => x.IsDefault).Name
        );
    }

    [Fact]
    public void Business_permissions_should_follow_the_fixed_privilege_hierarchy()
    {
        FixedRoleDefinition manager = GetRole(
            AuroraStruct3DAccessRoleDataSeedContributor.ManagerRoleName
        );
        FixedRoleDefinition operatorRole = GetRole(
            AuroraStruct3DAccessRoleDataSeedContributor.OperatorRoleName
        );
        FixedRoleDefinition viewer = GetRole(
            AuroraStruct3DAccessRoleDataSeedContributor.ViewerRoleName
        );

        Assert.Subset(
            manager.Permissions.ToHashSet(StringComparer.Ordinal),
            operatorRole.Permissions.ToHashSet(StringComparer.Ordinal)
        );
        Assert.Subset(
            operatorRole.Permissions.ToHashSet(StringComparer.Ordinal),
            viewer.Permissions.ToHashSet(StringComparer.Ordinal)
        );
        Assert.DoesNotContain("AuroraStruct3D.ProjectInfo.Delete", operatorRole.Permissions);
        Assert.DoesNotContain("AuroraStruct3D.ProductModel.CleanUp", operatorRole.Permissions);
        Assert.Contains("AuroraStruct3D.ProjectInfo.Delete", manager.Permissions);
        Assert.Contains("AuroraStruct3D.ProductModel.CleanUp", manager.Permissions);
    }

    [Fact]
    public void Fixed_role_mutation_permissions_should_be_recognized()
    {
        Assert.True(
            AuroraStruct3DAccessRoleDataSeedContributor.IsFixedRoleManagementPermission(
                "AbpIdentity.Roles.Create"
            )
        );
        Assert.True(
            AuroraStruct3DAccessRoleDataSeedContributor.IsFixedRoleManagementPermission(
                "AbpIdentity.Roles.ManagePermissions"
            )
        );
        Assert.False(
            AuroraStruct3DAccessRoleDataSeedContributor.IsFixedRoleManagementPermission(
                "AbpIdentity.Users"
            )
        );
    }

    [Fact]
    public void Fixed_users_should_map_one_to_one_to_fixed_roles()
    {
        FixedUserDefinition[] users =
            AuroraStruct3DAccessRoleDataSeedContributor.FixedUsers.ToArray();

        Assert.Equal(4, users.Length);
        Assert.Equal(
            ["admin", "manager", "operator", "viewer"],
            users.Select(x => x.UserName).ToArray()
        );
        Assert.Equal(4, users.Select(x => x.UserName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, users.Select(x => x.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            AuroraStruct3DAccessRoleDataSeedContributor.FixedRoles
                .Select(x => x.Name)
                .ToHashSet(StringComparer.Ordinal),
            users.Select(x => x.RoleName).ToHashSet(StringComparer.Ordinal)
        );
        Assert.False(
            Assert.Single(
                users,
                x =>
                    x.UserName
                    == AuroraStruct3DAccessRoleDataSeedContributor.DeveloperUserName
            ).ShouldChangePasswordOnNextLogin
        );
        Assert.All(
            users.Where(x =>
                x.UserName != AuroraStruct3DAccessRoleDataSeedContributor.DeveloperUserName
            ),
            x => Assert.True(x.ShouldChangePasswordOnNextLogin)
        );
    }

    private static FixedRoleDefinition GetRole(string name)
    {
        return Assert.Single(
            AuroraStruct3DAccessRoleDataSeedContributor.FixedRoles,
            x => x.Name == name
        );
    }
}
