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
    /// <summary>从产品三维数模库选择一个已就绪模型。</summary>
    ProductModelSelect,
    /// <summary>在已选择平面的局部 UV 坐标中编辑 ROI。</summary>
    PlaneRoiEditor,
}
