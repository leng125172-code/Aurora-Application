using AuroraStruct3D.Calibration;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Statements;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Operators;
using AuroraStruct3D.Workflow.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class ProductionScanWorkflowTests
{
    [Fact]
    public async Task Scan_operator_should_forward_configuration_and_publish_only_runtime_outputs()
    {
        Guid projectId = Guid.NewGuid();
        FakeScanService scanner = new(projectId, expectedCycles: 3);
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IProductionPointCloudScanService>(scanner)
            .BuildServiceProvider();
        using IDisposable ambient = WorkflowServiceProviderAmbient.Push(services);
        using WorkflowContext context = new();
        using scan_point_cloud op = new(
            projectId.ToString("D"),
            cycleCount: 3,
            timeoutSeconds: 30,
            enableTableFilter: false,
            tableClearanceMm: 5d
        );

        await op.ExecuteAsync(context);

        Assert.True(scanner.Called);
        PointCloudData cloud = Assert.IsType<PointCloudData>(context.Get("output_point_cloud"));
        Assert.Equal(2, context.Get<int>("point_count"));
        Assert.Equal(3, context.Get<int>("completed_cycles"));
        Assert.Equal(42L, context.Get<long>("duration_ms"));
        Assert.Equal(2, cloud.PointCount);
        cloud.DisposePointCloud();
    }

    [Fact]
    public async Task Scan_operator_should_propagate_workflow_cancellation()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IProductionPointCloudScanService>(new CancelAwareScanService())
            .BuildServiceProvider();
        using IDisposable ambient = WorkflowServiceProviderAmbient.Push(services);
        using WorkflowContext context = new();
        using scan_point_cloud op = new(Guid.NewGuid().ToString("D"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            op.ExecuteAsync(context, cancellation.Token)
        );
    }

    [Fact]
    public void Merge_should_concatenate_all_cycle_points_and_transfer_ownership()
    {
        PointCloudData first = Cloud(1f, 2f, 3f, 10);
        PointCloudData second = Cloud(4f, 5f, 6f, 20);

        PointCloudData merged = ProductionPointCloudScanService.MergePointClouds(
            [first, second]
        );

        Assert.Equal(2, merged.PointCount);
        Assert.Equal(1f, merged.PointCloud!.At<float>(0, 0));
        Assert.Equal(6f, merged.PointCloud.At<float>(1, 2));
        Assert.Equal((byte)20, merged.Colors!.At<byte>(1, 0));
        Assert.Null(first.PointCloud);
        Assert.Null(second.PointCloud);
        merged.DisposePointCloud();
    }

    [Fact]
    public void Merge_limit_should_release_every_cycle_cloud()
    {
        PointCloudData first = Cloud(1f, 2f, 3f, 10);
        PointCloudData second = Cloud(4f, 5f, 6f, 20);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ProductionPointCloudScanService.MergePointClouds(
                [first, second],
                maximumPointCount: 1,
                maximumBytes: ProductionPointCloudScanService.MaximumPointCloudBytes
            )
        );

        Assert.Contains("[SCAN_POINT_CLOUD_LIMIT]", error.Message);
        Assert.Null(first.PointCloud);
        Assert.Null(second.PointCloud);
    }

    [Fact]
    public async Task Kernel_should_cancel_an_active_async_node_as_stopped()
    {
        BlockingStatement statement = new();
        using WorkflowExecutionSession session = Session(statement, runId: Guid.NewGuid());
        WorkflowExecutionKernel kernel = new();

        Task execution = kernel.ExecuteToCompletionAsync(session);
        await statement.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, WorkflowRunCancellationRegistry.Cancel(session.RunId!.Value));
        await execution.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(WorkflowExecutionStatus.Stopped, session.Status);
        Assert.Equal(0, session.StepCursor);
    }

    [Fact]
    public async Task Long_running_timeout_policy_should_override_default_node_timeout()
    {
        DelayedStatement statement = new(TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(1));
        using WorkflowExecutionSession session = Session(statement);
        WorkflowExecutionKernel kernel = new(
            Options.Create(
                new WorkflowRuntimeSafetyOptions
                {
                    NodeTimeout = TimeSpan.FromMilliseconds(20),
                    WorkflowTimeout = TimeSpan.FromSeconds(2),
                }
            )
        );

        await kernel.ExecuteToCompletionAsync(session);

        Assert.Equal(WorkflowExecutionStatus.Completed, session.Status);
        Assert.Equal(1, session.StepCursor);
    }

    private static PointCloudData Cloud(float x, float y, float z, byte color)
    {
        Mat points = new(1, 3, MatType.CV_32FC1);
        points.Set(0, 0, x);
        points.Set(0, 1, y);
        points.Set(0, 2, z);
        Mat colors = new(1, 3, MatType.CV_8UC1, new Scalar(color));
        PointCloudData cloud = new() { Value = points };
        cloud.SetColors(colors);
        return cloud;
    }

    private static WorkflowExecutionSession Session(
        IWorkflowStatement statement,
        Guid? runId = null
    )
    {
        WorkflowContext context = new();
        WorkflowExecutionVariablePool variables = new([]);
        variables.Initialize(context);
        return new WorkflowExecutionSession
        {
            ExecutionId = Guid.NewGuid(),
            RunId = runId,
            ProjectId = Guid.NewGuid(),
            WorkflowId = Guid.NewGuid(),
            WorkflowName = "production-scan-test",
            Mode = WorkflowExecutionMode.RunOnce,
            RuntimeWorkflow = new WorkflowDefinition("production-scan-test", [statement]),
            StatementNodeIds = ["scan"],
            Context = context,
            VariablePool = variables,
        };
    }

    private sealed class FakeScanService(Guid projectId, int expectedCycles)
        : IProductionPointCloudScanService
    {
        public bool Called { get; private set; }

        public Task<ProductionPointCloudScanResult> ScanAsync(
            ProductionPointCloudScanRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Assert.Equal(projectId, request.CalibProjectId);
            Assert.Equal(expectedCycles, request.CycleCount);
            Assert.False(request.EnableTableFilter);
            Assert.Equal(5d, request.TableClearanceMm);
            Called = true;
            PointCloudData cloud = Cloud(1f, 2f, 3f, 1);
            PointCloudData second = Cloud(4f, 5f, 6f, 2);
            PointCloudData merged = ProductionPointCloudScanService.MergePointClouds(
                [cloud, second]
            );
            return Task.FromResult(
                new ProductionPointCloudScanResult(merged, 2, expectedCycles, 42)
            );
        }
    }

    private sealed class CancelAwareScanService : IProductionPointCloudScanService
    {
        public async Task<ProductionPointCloudScanResult> ScanAsync(
            ProductionPointCloudScanRequest request,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class BlockingStatement : IWorkflowStatement
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public void Execute(IWorkflowContext context) =>
            throw new NotSupportedException("异步测试语句不应走同步入口。");

        public async Task ExecuteAsync(
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class DelayedStatement(TimeSpan delay, TimeSpan timeout)
        : IWorkflowStatement,
            IWorkflowStatementTimeoutProvider
    {
        public void Execute(IWorkflowContext context) =>
            throw new NotSupportedException("异步测试语句不应走同步入口。");

        public Task ExecuteAsync(
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        ) => Task.Delay(delay, cancellationToken);

        public TimeSpan? GetRequestedTimeout(IWorkflowContext context) => timeout;
    }
}
