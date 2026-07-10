using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.Workflow.Runtime;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

/// <summary>
/// ROI 底图预运行调度单元测试：验证祖先子图仅包含目标节点真正依赖的上游算子，
/// 天然跳过并行分支上的副作用节点（如“存 Blob”）。
/// </summary>
public class WorkflowAncestorScheduleTests
{
    private static NodeModel Node(string id, string type) => new() { Id = id, Type = type };

    private static EdgeModel Edge(string source, string target) =>
        new()
        {
            Id = $"{source}->{target}",
            SourceNodeId = source,
            TargetNodeId = target,
        };

    /// <summary>
    /// 构造与固定 ROI 高度差工作流一致的拓扑：
    /// start → read → downsample → colorize
    /// colorize → save_blob（并行副作用分支）
    /// colorize → preview → roi_a → crop_a
    /// </summary>
    private static GraphDataModel BuildHeightDiffGraph()
    {
        return new GraphDataModel
        {
            Nodes =
            [
                Node("start", "start-node"),
                Node("read", "op-read"),
                Node("downsample", "op-downsample"),
                Node("colorize", "op-colorize"),
                Node("save_blob", "op-save-blob"),
                Node("preview", "op-colored-to-image"),
                Node("roi_a", "op-roi-partition"),
                Node("crop_a", "op-point-cloud-crop"),
                Node("end", "end-node"),
            ],
            Edges =
            [
                Edge("start", "read"),
                Edge("read", "downsample"),
                Edge("downsample", "colorize"),
                Edge("colorize", "save_blob"),
                Edge("colorize", "preview"),
                Edge("preview", "roi_a"),
                Edge("roi_a", "crop_a"),
                Edge("crop_a", "end"),
            ],
        };
    }

    [Fact]
    public void BuildAncestorNodeOrder_Should_Exclude_SideEffect_Branch()
    {
        GraphDataModel graph = BuildHeightDiffGraph();

        IReadOnlyList<string> ancestors = WorkflowNodeScheduleBuilder.BuildAncestorNodeOrder(
            graph,
            "roi_a"
        );

        // 只应包含 ROI 真正依赖的上游，且不含并行副作用节点 save_blob。
        Assert.Equal(new[] { "read", "downsample", "colorize", "preview" }, ancestors);
        Assert.DoesNotContain("save_blob", ancestors);
    }

    [Fact]
    public void BuildAncestorNodeOrder_Should_Exclude_Target_And_Terminals()
    {
        GraphDataModel graph = BuildHeightDiffGraph();

        IReadOnlyList<string> ancestors = WorkflowNodeScheduleBuilder.BuildAncestorNodeOrder(
            graph,
            "roi_a"
        );

        Assert.DoesNotContain("roi_a", ancestors);
        Assert.DoesNotContain("crop_a", ancestors);
        Assert.DoesNotContain("start", ancestors);
        Assert.DoesNotContain("end", ancestors);
    }

    [Fact]
    public void BuildAncestorNodeOrder_Should_Return_Empty_For_First_Operator()
    {
        GraphDataModel graph = BuildHeightDiffGraph();

        IReadOnlyList<string> ancestors = WorkflowNodeScheduleBuilder.BuildAncestorNodeOrder(
            graph,
            "read"
        );

        Assert.Empty(ancestors);
    }

    [Fact]
    public void BuildAncestorNodeOrder_Should_Preserve_Topological_Order()
    {
        GraphDataModel graph = BuildHeightDiffGraph();

        IReadOnlyList<string> ancestors = WorkflowNodeScheduleBuilder.BuildAncestorNodeOrder(
            graph,
            "crop_a"
        );

        int idxDownsample = ancestors.ToList().IndexOf("downsample");
        int idxColorize = ancestors.ToList().IndexOf("colorize");
        int idxPreview = ancestors.ToList().IndexOf("preview");
        int idxRoi = ancestors.ToList().IndexOf("roi_a");

        Assert.True(idxDownsample < idxColorize);
        Assert.True(idxColorize < idxPreview);
        Assert.True(idxPreview < idxRoi);
        Assert.DoesNotContain("save_blob", ancestors);
    }
}
