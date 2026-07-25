using System.Text.Json;

namespace AuroraStruct3D.OpenCV.Workflow.Operators;

/// <summary>
/// 汇总多个检测分支的 OK/NG 与结构化结果，形成稳定的工作流业务出口。
/// </summary>
[Guid("7f3a2c10-6b95-4e8c-9a41-2d53f670c901")]
[Category("流程控制")]
[DisplayName("检测结果汇总")]
[Description("汇总平面度、角度和圆形检测结果，输出整体 OK/NG 及结构化 JSON。")]
public sealed class combine_inspection_results : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            BoolInput("flatness_ok", "平面度是否合格"),
            BoolInput("line_ok", "角度是否合格"),
            BoolInput("circle_ok", "圆形是否合格"),
            StringInput("flatness_result", "平面度结果"),
            StringInput("line_result", "角度结果"),
            StringInput("circle_result", "圆形结果"),
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new VisionParameter<bool>
            {
                ParameterName = "is_ok",
                DisplayName = "整体是否合格",
                ParameterType = typeof(bool),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<string>
            {
                ParameterName = "result_json",
                DisplayName = "检测汇总结果",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    public void Execute(IWorkflowContext context)
    {
        bool flatnessOk = context.Get<bool>("flatness_ok");
        bool lineOk = context.Get<bool>("line_ok");
        bool circleOk = context.Get<bool>("circle_ok");
        bool isOk = flatnessOk && lineOk && circleOk;

        context.Set("is_ok", isOk);
        context.Set(
            "result_json",
            JsonSerializer.Serialize(
                new
                {
                    isOk,
                    resultCode = isOk ? "OK" : "NG",
                    measurements = new
                    {
                        flatness = ParseJson(context.Get<string>("flatness_result")),
                        line = ParseJson(context.Get<string>("line_result")),
                        circle = ParseJson(context.Get<string>("circle_result")),
                    },
                }
            )
        );
    }

    public void Dispose() { }

    private static VisionParameter<bool> BoolInput(string name, string displayName) =>
        new()
        {
            ParameterName = name,
            DisplayName = displayName,
            ParameterType = typeof(bool),
        };

    private static VisionParameter<string> StringInput(string name, string displayName) =>
        new()
        {
            ParameterName = name,
            DisplayName = displayName,
            ParameterType = typeof(string),
        };

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
