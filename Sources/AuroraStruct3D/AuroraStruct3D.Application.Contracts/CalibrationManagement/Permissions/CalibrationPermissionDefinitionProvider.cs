namespace AuroraStruct3D.Permissions;

/// <summary>
/// 标定管理权限定义提供程序。
/// 注册到 ABP 权限系统，按"标定设备 / 标定工程 / 参数模板 / 标定结果"四个子分组组织。
/// </summary>
public class CalibrationPermissionDefinitionProvider : PermissionDefinitionProvider
{
    /// <inheritdoc/>
    public override void Define(IPermissionDefinitionContext context)
    {
        PermissionGroupDefinition group = context.AddGroup(
            CalibrationPermissions.GroupName,
            L("Permission:Calibration")
        );

        // ── 标定设备 ─────────────────────────────────────────────────────
        PermissionDefinition device = group.AddPermission(
            CalibrationPermissions.Device,
            L("Permission:Calibration.Device")
        );
        device.AddChild(
            CalibrationPermissions.DeviceCreate,
            L("Permission:Calibration.Device.Create")
        );
        device.AddChild(
            CalibrationPermissions.DeviceUpdate,
            L("Permission:Calibration.Device.Update")
        );
        device.AddChild(
            CalibrationPermissions.DeviceDelete,
            L("Permission:Calibration.Device.Delete")
        );
        device.AddChild(CalibrationPermissions.DeviceBind, L("Permission:Calibration.Device.Bind"));

        // ── 标定工程 ─────────────────────────────────────────────────────
        PermissionDefinition project = group.AddPermission(
            CalibrationPermissions.Project,
            L("Permission:Calibration.Project")
        );
        project.AddChild(
            CalibrationPermissions.ProjectCreate,
            L("Permission:Calibration.Project.Create")
        );
        project.AddChild(
            CalibrationPermissions.ProjectUpdate,
            L("Permission:Calibration.Project.Update")
        );
        project.AddChild(
            CalibrationPermissions.ProjectDelete,
            L("Permission:Calibration.Project.Delete")
        );
        project.AddChild(
            CalibrationPermissions.ProjectCapture,
            L("Permission:Calibration.Project.Capture")
        );
        project.AddChild(
            CalibrationPermissions.ProjectCompute,
            L("Permission:Calibration.Project.Compute")
        );
        project.AddChild(
            CalibrationPermissions.ProjectValidate,
            L("Permission:Calibration.Project.Validate")
        );
        project.AddChild(
            CalibrationPermissions.ProjectExport,
            L("Permission:Calibration.Project.Export")
        );
        project.AddChild(
            CalibrationPermissions.ProjectArchive,
            L("Permission:Calibration.Project.Archive")
        );

        // ── 参数模板 ─────────────────────────────────────────────────────
        PermissionDefinition template = group.AddPermission(
            CalibrationPermissions.Template,
            L("Permission:Calibration.Template")
        );
        template.AddChild(
            CalibrationPermissions.TemplateCreate,
            L("Permission:Calibration.Template.Create")
        );
        template.AddChild(
            CalibrationPermissions.TemplateUpdate,
            L("Permission:Calibration.Template.Update")
        );
        template.AddChild(
            CalibrationPermissions.TemplateDelete,
            L("Permission:Calibration.Template.Delete")
        );

        // ── 标定结果 ─────────────────────────────────────────────────────
        PermissionDefinition result = group.AddPermission(
            CalibrationPermissions.Result,
            L("Permission:Calibration.Result")
        );
        result.AddChild(
            CalibrationPermissions.ResultSetActive,
            L("Permission:Calibration.Result.SetActive")
        );
        result.AddChild(
            CalibrationPermissions.ResultExport,
            L("Permission:Calibration.Result.Export")
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AuroraStruct3DResource>(name);
    }
}
