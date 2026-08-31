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
        [InspectionResults.Output<HeightDiffInspectionDetails>("高度差判定结果")];

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

        double heightA = RequireHeight(context, "height_a", "区域A高度");
        double heightB = RequireHeight(context, "height_b", "区域B高度");
        double signedDiff = Math.Round(heightA - heightB, 6);
        double absDiff = Math.Round(Math.Abs(signedDiff), 6);
        bool isOk = signedDiff >= _minDiff && signedDiff <= _maxDiff;

        var result = new HeightDiffInspectionDetails(
            Math.Round(heightA, 6),
            Math.Round(heightB, 6),
            signedDiff,
            absDiff,
            _minDiff,
            _maxDiff,
            isOk);

        context.Set("result", InspectionResults.CreateTyped(true, isOk, result));
    }

    private static double RequireHeight(
        IWorkflowContext context,
        string parameterName,
        string displayName
    )
    {
        object? raw = context.Get(parameterName);
        if (raw is null)
        {
            throw new InvalidOperationException(
                $"{displayName}未连接。请将 {parameterName} 绑定到高度统计节点的数值输出。"
            );
        }

        try
        {
            double value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
            if (!double.IsFinite(value))
                throw new InvalidOperationException($"{displayName}不是有效有限数值。");
            return value;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException(
                $"{displayName}不是有效数值；当前绑定值为 '{raw}'。请检查输入绑定来源是否为变量。",
                exception
            );
        }
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
