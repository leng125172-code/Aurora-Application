namespace AuroraStruct3D.Permissions;

/// <summary>
/// 应用级访问权限名称，放在 Domain.Shared 供权限定义、种子数据和业务层共同使用。
/// </summary>
public static class AuroraStruct3DAccessPermissions
{
    public const string GroupName = "AuroraStruct3D.Access";

    public const string Operation = GroupName + ".Operation";

    public const string Management = GroupName + ".Management";
}
