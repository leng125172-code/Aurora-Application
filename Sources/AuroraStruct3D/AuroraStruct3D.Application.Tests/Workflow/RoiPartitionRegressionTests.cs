using AuroraStruct3D.OpenCV.RoiOps;
using AuroraStruct3D.OpenCV.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class RoiPartitionRegressionTests
{
    [Fact]
    public void Mixed_Rois_From_Editor_Should_Generate_Masks_And_Primary_Mask()
    {
        const string roiJson =
            """
            {"rois":[
              {"type":"Circle","cx":76,"cy":279,"r":9,"name":"d"},
              {"type":"Rect","x":239,"y":313,"width":26,"height":20,"rotation":0,"name":"e"},
              {"name":"f","type":"Path","d":"M227,215 L129,226 L120,318 Z"},
              {"name":"g","type":"Sector","cx":120,"cy":341,
               "startPoint":{"x":157,"y":345},"endPoint":{"x":122,"y":378}}
            ]}
            """;
        using Mat image = Mat.Zeros(422, 512, MatType.CV_8UC4);
        using WorkflowContext context = new();
        context.Set("input_mat", image);
        using var op = new roi_partition(roiJson);

        op.Execute(context);

        List<Mat> masks = Assert.IsType<List<Mat>>(context.Get<List<Mat>>("output_masks"));
        Assert.Equal(4, masks.Count);
        Assert.NotNull(context.Get<Mat>("primary_mask"));
        foreach (Mat mask in masks)
            mask.Dispose();
    }
}
