namespace AuroraStruct3D.Workflow;

/// <summary>
/// 端口控件类型，用于前端决定渲染何种 UI 控件。
/// </summary>
public enum PortControlType
{
    Input,
    Select,
    ImageUpload,
    PointCloudUpload,
    Download,
    Switch,
    Number,
    Color,
    Variable,
}
