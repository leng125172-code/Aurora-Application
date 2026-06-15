namespace AuroraStruct3D.Permissions;

/// <summary>
/// 项目管理权限常量定义
/// </summary>
public static class ProjectInfoPermissions
{
    /// <summary>权限组名称</summary>
    public const string GroupName = "AuroraStruct3D.ProjectInfo";

    /// <summary>查看项目列表/详情</summary>
    public const string Default = "AuroraStruct3D.ProjectInfo";

    /// <summary>创建新项目</summary>
    public const string Create = "AuroraStruct3D.ProjectInfo.Create";

    /// <summary>修改项目信息</summary>
    public const string Update = "AuroraStruct3D.ProjectInfo.Update";

    /// <summary>变更项目状态</summary>
    public const string ChangeStatus = "AuroraStruct3D.ProjectInfo.ChangeStatus";

    /// <summary>删除项目</summary>
    public const string Delete = "AuroraStruct3D.ProjectInfo.Delete";
}
