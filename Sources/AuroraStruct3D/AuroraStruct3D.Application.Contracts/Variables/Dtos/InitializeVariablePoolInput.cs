using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 运行时在线变量池初始化输入。
/// </summary>
public class InitializeVariablePoolInput
{
    /// <summary>项目 ID（隔离边界）。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>执行实例 ID。</summary>
    [Required]
    public Guid InstanceId { get; set; }

    /// <summary>当前实例绑定的变量快照版本号。</summary>
    public long SnapshotVersion { get; set; }

    /// <summary>实例启动时需要覆盖默认值的种子集合。</summary>
    public List<VariableRuntimeSeedDto> Seeds { get; set; } = new();
}
