namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定模块全局常量定义。
/// 集中维护数据库表前缀、字段长度限制、BLOB 容器名等。
/// </summary>
public static class CalibrationManagementConsts
{
    /// <summary>
    /// 数据库表名在模块内的二级前缀。
    /// 最终表名 = <c>AuroraStruct3DDbProperties.DbTablePrefix</c>（“AbpPro”）+ <see cref="ModuleTableSegment"/> + 实体复数名。
    /// 例：AbpProCalibDevices / AbpProCalibProjects 。
    /// </summary>
    public const string ModuleTableSegment = "Calib";

    /// <summary>BLOB 容器名称：用于存储标定采集的原始图像</summary>
    public const string BlobContainerName = "calibration-images";

    /// <summary>名称类字段最大长度（设备名、工程名、模板名等）</summary>
    public const int MaxNameLength = 128;

    /// <summary>描述/备注最大长度</summary>
    public const int MaxDescriptionLength = 512;

    /// <summary>BLOB 键名最大长度（容纳 "{projectId}/frame-NNNN/camera-{cameraId}.png" 形式）</summary>
    public const int MaxBlobNameLength = 256;

    /// <summary>失败原因/错误信息最大长度</summary>
    public const int MaxErrorMessageLength = 2048;

    /// <summary>JSON 列最大长度提示（实际使用 text 列，无硬限制）</summary>
    public const int MaxJsonContentLength = 1024 * 1024;

    /// <summary>AprilTag 标签家族名称最大长度</summary>
    public const int MaxAprilTagFamilyLength = 64;

    /// <summary>角色字符串字段最大长度（云台预设名等）</summary>
    public const int MaxRoleLabelLength = 64;
}
