using System.Text.Json.Nodes;

namespace AuroraStruct3D.OpenCV.Workflow.Operators;

[Guid("7f3a2c10-6b95-4e8c-9a41-2d53f670c902")]
[Category("流程控制")]
[DisplayName("产品检查结果")]
[Description("将综合检查和安装检查组合为唯一产品结果对象。")]
public sealed class build_product_inspection_result : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
    [
        Input("comprehensive"), Input("installation"),
        Text("point_cloud_url"), Text("projection_image_url"),
        Text("flatness_heatmap_url"), Text("installation_image_url"),
    ];
    public static List<IVisionParameter>? OutputVisionParameters =>
    [
        new VisionParameter<ProductInspectionResult>
        {
            ParameterName = "result", DisplayName = "产品检查结果",
            ParameterType = typeof(ProductInspectionResult), JsonSchema = InspectionResultSchema.Product,
        },
    ];
    public static List<IConfigParameter>? ConfigParameters => null;

    public void Execute(IWorkflowContext context)
    {
        InspectionResultBase comprehensive = context.Get<InspectionResultBase>("comprehensive") ?? Unknown("Missing comprehensive result");
        InspectionResultBase installation = context.Get<InspectionResultBase>("installation") ?? Unknown("Missing installation result");
        bool valid = comprehensive.isValid && installation.isValid;
        bool ok = valid && comprehensive.isOk && installation.isOk;
        List<InspectionArtifact> artifacts = [];
        AddArtifact(artifacts, "point-cloud", context.Get<string>("point_cloud_url"), "model/ply");
        AddArtifact(artifacts, "projection", context.Get<string>("projection_image_url"), "image/png");
        AddArtifact(artifacts, "flatness-heatmap", context.Get<string>("flatness_heatmap_url"), "image/png");
        AddArtifact(artifacts, "installation-view", context.Get<string>("installation_image_url"), "image/png");
        context.Set("result", new ProductInspectionResult
        {
            isValid = valid, isOk = ok,
            resultCode = !valid ? InspectionResultCode.UNKNOWN : ok ? InspectionResultCode.OK : InspectionResultCode.NG,
            message = ok ? "Product inspection passed" : "Product inspection failed",
            reasons = comprehensive.reasons.Concat(installation.reasons).ToList(),
            comprehensive = comprehensive.ToNode(), installation = installation.ToNode(),
            artifacts = artifacts,
        });
    }
    private static VisionParameter<InspectionResultBase> Input(string name) => new() { ParameterName = name, ParameterType = typeof(InspectionResultBase), JsonSchema = InspectionResultSchema.Inspection };
    private static VisionParameter<string> Text(string name) => new() { ParameterName = name, ParameterType = typeof(string) };
    private static void AddArtifact(List<InspectionArtifact> artifacts, string name, string? url, string mediaType)
    {
        if (!string.IsNullOrWhiteSpace(url)) artifacts.Add(new InspectionArtifact { name = name, url = url, mediaType = mediaType });
    }
    private static InspectionResultBase Unknown(string reason) => new InspectionResult<JsonNode?>
        { resultCode = InspectionResultCode.UNKNOWN, message = reason, reasons = [reason] };
    public void Dispose() { }
}
