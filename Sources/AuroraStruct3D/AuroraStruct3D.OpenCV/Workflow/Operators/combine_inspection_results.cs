namespace AuroraStruct3D.OpenCV.Workflow.Operators;

[Guid("7f3a2c10-6b95-4e8c-9a41-2d53f670c901")]
[Category("流程控制")]
[DisplayName("检测结果汇总")]
[Description("将三个检测分支汇总为唯一强类型 InspectionResult。")]
public sealed class combine_inspection_results : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
    [
        Result("flatness_result"), Result("line_result"), Result("circle_result"),
    ];

    public static List<IVisionParameter>? OutputVisionParameters =>
    [
        InspectionResults.Output<CombinedInspectionDetails>("检测结果"),
    ];

    public static List<IConfigParameter>? ConfigParameters => null;

    public void Execute(IWorkflowContext context)
    {
        InspectionResultBase flatness = Required(context, "flatness_result");
        InspectionResultBase line = Required(context, "line_result");
        InspectionResultBase circle = Required(context, "circle_result");
        InspectionResultBase[] branch = [flatness, line, circle];
        bool isValid = branch.All(x => x.isValid);
        bool isOk = isValid && branch.All(x => x.isOk);
        context.Set("result", InspectionResults.CreateTyped(isValid, isOk,
            new CombinedInspectionDetails(flatness, line, circle),
            isOk ? "Inspection passed" : "Inspection failed"));
    }

    private static VisionParameter<InspectionResultBase> Result(string name) => new()
        { ParameterName = name, ParameterType = typeof(InspectionResultBase), JsonSchema = InspectionResultSchema.Inspection };
    private static InspectionResultBase Required(IWorkflowContext context, string name) =>
        context.Get<InspectionResultBase>(name) ?? throw new InvalidOperationException($"检测结果 '{name}' 为空。");
    public void Dispose() { }
}
