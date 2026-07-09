using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 计算两个区域的带符号高度差并做区间判定。
/// </summary>
[Guid("8c3dd5d9-8234-49fb-8d1f-1d7c7e0b4201")]
[Category("3D拟合测量")]
[DisplayName("高度差判定")]
[Description("计算两个区域的带符号高度差，并判断是否落在给定阈值区间。")]
public class height_diff_range_eval : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new VisionParameter<double>
            {
                ParameterName = "height_a",
                ParameterType = typeof(double),
                DisplayName = "区域A高度",
            },
            new VisionParameter<double>
            {
                ParameterName = "height_b",
                ParameterType = typeof(double),
                DisplayName = "区域B高度",
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new VisionParameter<double>
            {
                ParameterName = "signed_diff",
                ParameterType = typeof(double),
                DisplayName = "带符号差值",
            },
            new VisionParameter<double>
            {
                ParameterName = "abs_diff",
                ParameterType = typeof(double),
                DisplayName = "绝对差值",
            },
            new VisionParameter<bool>
            {
                ParameterName = "is_ok",
                ParameterType = typeof(bool),
                DisplayName = "是否OK",
            },
            new VisionParameter<string>
            {
                ParameterName = "result_json",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
                DisplayName = "判定结果",
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "minDiff",
                DisplayName = "最小差值",
                ParameterType = typeof(double),
                DefaultValue = "-0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxDiff",
                DisplayName = "最大差值",
                ParameterType = typeof(double),
                DefaultValue = "0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _minDiff;
    private readonly double _maxDiff;
    private bool _disposed;

    public height_diff_range_eval(double minDiff = -0.5, double maxDiff = 0.5)
    {
        _minDiff = minDiff;
        _maxDiff = maxDiff;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        double heightA = context.Get<double>("height_a");
        double heightB = context.Get<double>("height_b");
        double signedDiff = Math.Round(heightA - heightB, 6);
        double absDiff = Math.Round(Math.Abs(signedDiff), 6);
        bool isOk = signedDiff >= _minDiff && signedDiff <= _maxDiff;

        var result = new
        {
            heightA = Math.Round(heightA, 6),
            heightB = Math.Round(heightB, 6),
            signedDiff,
            absDiff,
            minDiff = _minDiff,
            maxDiff = _maxDiff,
            isOk,
        };

        context.Set("signed_diff", signedDiff);
        context.Set("abs_diff", absDiff);
        context.Set("is_ok", isOk);
        context.Set("result_json", JsonSerializer.Serialize(result));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
