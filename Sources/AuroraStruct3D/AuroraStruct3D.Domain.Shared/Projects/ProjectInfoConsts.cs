namespace AuroraStruct3D.Projects;

/// <summary>
/// 项目主表相关常量定义（放置于 Domain.Shared，供 Contracts 层使用）
/// </summary>
public static class ProjectInfoConsts
{
    /// <summary>项目编号最大长度</summary>
    public const int MaxProjectCodeLength = 64;

    /// <summary>项目名称最大长度</summary>
    public const int MaxNameLength = 256;

    /// <summary>版本号最大长度</summary>
    public const int MaxVersionLength = 32;

    /// <summary>描述最大长度</summary>
    public const int MaxDescriptionLength = 2000;
}
