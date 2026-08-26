using System.ComponentModel;
using System.Runtime.InteropServices;
using AuroraStruct3D.Calibration;
using AuroraStruct3D.OpenCV;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.Workflow.Operators;

/// <summary>在工作流运行期主动完成指定周期数的生产扫描并直接输出内存点云。</summary>
[Guid("3a15ec1c-6978-4b72-9e6a-e749387b3f11")]
[Category("3D采集")]
[DisplayName("生产3D扫描")]
[Description("按标定项目主动采集 N 个周期并输出点云；不使用标定预览会话，也不自动保存文件。")]
[WorkflowNodeTimeout("timeoutSeconds")]
public sealed class scan_point_cloud : IOperator, IAsyncWorkflowOperator
{
    public static List<IVisionParameter>? InputVisionParameters => [];

    public static List<IVisionParameter>? OutputVisionParameters =>
    [
        new PointCloudData
        {
            ParameterName = "output_point_cloud",
            DisplayName = "扫描点云",
            Description = "本次 N 周期扫描合并后的内存点云",
        },
        new VisionParameter<int>
        {
            ParameterName = "point_count",
            DisplayName = "点数",
            ParameterType = typeof(int),
        },
        new VisionParameter<int>
        {
            ParameterName = "completed_cycles",
            DisplayName = "完成周期",
            ParameterType = typeof(int),
        },
        new VisionParameter<long>
        {
            ParameterName = "duration_ms",
            DisplayName = "耗时(ms)",
            ParameterType = typeof(long),
        },
    ];

    public static List<IConfigParameter>? ConfigParameters =>
    [
        new ConfigParameter
        {
            Name = "calibProjectId",
            DisplayName = "标定项目",
            Description = "选择已完成双目标定、且绑定设备与标定结果一致的项目",
            ParameterType = typeof(string),
            Required = true,
            ControlType = PortControlType.CalibProjectSelect,
        },
        new ConfigParameter
        {
            Name = "cycleCount",
            DisplayName = "扫描周期",
            Description = "每次算子调用连续采集并重建的周期数",
            ParameterType = typeof(int),
            DefaultValue = "1",
            ValueLimit = new[] { 1, 20 },
            ControlType = PortControlType.Number,
        },
        new ConfigParameter
        {
            Name = "timeoutSeconds",
            DisplayName = "超时(秒)",
            ParameterType = typeof(int),
            DefaultValue = "300",
            ValueLimit = new[] { 10, 3600 },
            ControlType = PortControlType.Number,
        },
        new ConfigParameter
        {
            Name = "enableTableFilter",
            DisplayName = "过滤台面",
            ParameterType = typeof(bool),
            DefaultValue = "true",
            ControlType = PortControlType.Switch,
        },
        new ConfigParameter
        {
            Name = "tableClearanceMm",
            DisplayName = "台面净空(mm)",
            ParameterType = typeof(double),
            DefaultValue = "3",
            ValueLimit = new[] { 0d, 50d },
            ControlType = PortControlType.Number,
        },
    ];

    private readonly Guid _calibProjectId;
    private readonly int _cycleCount;
    private readonly int _timeoutSeconds;
    private readonly bool _enableTableFilter;
    private readonly double _tableClearanceMm;
    private bool _disposed;

    public scan_point_cloud(
        string calibProjectId,
        int cycleCount = 1,
        int timeoutSeconds = 300,
        bool enableTableFilter = true,
        double tableClearanceMm = 3d
    )
    {
        if (!Guid.TryParse(calibProjectId, out _calibProjectId) || _calibProjectId == Guid.Empty)
            throw new ArgumentException("必须选择有效的标定项目。", nameof(calibProjectId));
        if (cycleCount is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(cycleCount), "扫描周期必须在 1 到 20 之间。");
        if (timeoutSeconds is < 10 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "超时必须在 10 到 3600 秒之间。");
        if (tableClearanceMm is < 0 or > 50)
            throw new ArgumentOutOfRangeException(
                nameof(tableClearanceMm),
                "台面净空必须在 0 到 50 mm 之间。"
            );
        _cycleCount = cycleCount;
        _timeoutSeconds = timeoutSeconds;
        _enableTableFilter = enableTableFilter;
        _tableClearanceMm = tableClearanceMm;
    }

    public void Execute(IWorkflowContext context) =>
        ExecuteAsync(context).GetAwaiter().GetResult();

    public async Task ExecuteAsync(
        IWorkflowContext context,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IServiceProvider services = WorkflowServiceProviderAmbient.Current;
        IProductionPointCloudScanService scanner =
            services.GetRequiredService<IProductionPointCloudScanService>();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

        ProductionPointCloudScanResult result;
        try
        {
            result = await scanner.ScanAsync(
                new ProductionPointCloudScanRequest(
                    _calibProjectId,
                    _cycleCount,
                    _enableTableFilter,
                    _tableClearanceMm
                ),
                timeout.Token
            );
        }
        catch (OperationCanceledException ex)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"[SCAN_TIMEOUT] 扫描超过 {_timeoutSeconds} 秒，已取消。",
                ex
            );
        }

        context.Set("output_point_cloud", result.PointCloud);
        context.Set("point_count", result.PointCount);
        context.Set("completed_cycles", result.CompletedCycles);
        context.Set("duration_ms", result.DurationMs);
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
