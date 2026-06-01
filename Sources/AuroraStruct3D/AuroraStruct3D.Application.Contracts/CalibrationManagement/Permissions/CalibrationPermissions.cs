namespace AuroraStruct3D.Permissions;

/// <summary>
/// 标定管理权限常量定义。
/// 分组：AuroraStruct3D.Calibration
/// 子分组：Device / Project / Template / Result
/// </summary>
public static class CalibrationPermissions
{
    /// <summary>权限组名称</summary>
    public const string GroupName = "AuroraStruct3D.Calibration";

    // ── 标定设备配置 ─────────────────────────────────────────────────────
    /// <summary>查看标定设备配置</summary>
    public const string Device = "AuroraStruct3D.Calibration.Device";

    /// <summary>创建标定设备配置</summary>
    public const string DeviceCreate = "AuroraStruct3D.Calibration.Device.Create";

    /// <summary>修改标定设备配置</summary>
    public const string DeviceUpdate = "AuroraStruct3D.Calibration.Device.Update";

    /// <summary>删除标定设备配置</summary>
    public const string DeviceDelete = "AuroraStruct3D.Calibration.Device.Delete";

    /// <summary>调整相机/电机/投射器绑定</summary>
    public const string DeviceBind = "AuroraStruct3D.Calibration.Device.Bind";

    // ── 标定工程 ─────────────────────────────────────────────────────────
    /// <summary>查看标定工程</summary>
    public const string Project = "AuroraStruct3D.Calibration.Project";

    /// <summary>创建标定工程</summary>
    public const string ProjectCreate = "AuroraStruct3D.Calibration.Project.Create";

    /// <summary>修改标定工程</summary>
    public const string ProjectUpdate = "AuroraStruct3D.Calibration.Project.Update";

    /// <summary>删除标定工程</summary>
    public const string ProjectDelete = "AuroraStruct3D.Calibration.Project.Delete";

    /// <summary>执行标定图像采集</summary>
    public const string ProjectCapture = "AuroraStruct3D.Calibration.Project.Capture";

    /// <summary>执行标定计算（内参/外参/结构光）</summary>
    public const string ProjectCompute = "AuroraStruct3D.Calibration.Project.Compute";

    /// <summary>执行标定结果验证</summary>
    public const string ProjectValidate = "AuroraStruct3D.Calibration.Project.Validate";

    /// <summary>导出标定参数与报告</summary>
    public const string ProjectExport = "AuroraStruct3D.Calibration.Project.Export";

    /// <summary>归档标定工程</summary>
    public const string ProjectArchive = "AuroraStruct3D.Calibration.Project.Archive";

    // ── 参数模板 ─────────────────────────────────────────────────────────
    /// <summary>查看参数模板</summary>
    public const string Template = "AuroraStruct3D.Calibration.Template";

    /// <summary>创建参数模板</summary>
    public const string TemplateCreate = "AuroraStruct3D.Calibration.Template.Create";

    /// <summary>修改参数模板</summary>
    public const string TemplateUpdate = "AuroraStruct3D.Calibration.Template.Update";

    /// <summary>删除参数模板</summary>
    public const string TemplateDelete = "AuroraStruct3D.Calibration.Template.Delete";

    // ── 标定结果 ─────────────────────────────────────────────────────────
    /// <summary>查看标定结果</summary>
    public const string Result = "AuroraStruct3D.Calibration.Result";

    /// <summary>切换生效版本</summary>
    public const string ResultSetActive = "AuroraStruct3D.Calibration.Result.SetActive";

    /// <summary>导出标定结果</summary>
    public const string ResultExport = "AuroraStruct3D.Calibration.Result.Export";
}
