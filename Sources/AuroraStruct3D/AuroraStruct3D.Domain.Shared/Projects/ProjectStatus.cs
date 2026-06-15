namespace AuroraStruct3D.Projects;

/// <summary>
/// 项目状态枚举（放置于 Domain.Shared，供 Contracts 层使用）
/// </summary>
public enum ProjectStatus
{
    /// <summary>进行中</summary>
    Active = 0,

    /// <summary>已暂停</summary>
    Suspended = 1,

    /// <summary>已完成</summary>
    Completed = 2,

    /// <summary>已归档</summary>
    Archived = 3,
}
