using System.Text.Json;
using AuroraStruct3D.OpenCV.GeometryOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class ChessboardQualityAssessmentOperatorTests
{
    [Fact]
    public void PortDefinitions_Should_Expose_Quality_Result_Ports()
    {
        List<IVisionParameter>? inputs = chessboard_quality_assessment.InputVisionParameters;
        List<IVisionParameter>? outputs = chessboard_quality_assessment.OutputVisionParameters;
        List<IConfigParameter>? configs = chessboard_quality_assessment.ConfigParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.NotNull(configs);

        MatImg input = Assert.IsType<MatImg>(Assert.Single(inputs!));
        Assert.Equal("input_mat", input.ParameterName);

        Assert.Collection(
            outputs!,
            output =>
            {
                MatImg port = Assert.IsType<MatImg>(output);
                Assert.Equal("output_mat", port.ParameterName);
            },
            output =>
            {
                VisionParameter<string> port = Assert.IsType<VisionParameter<string>>(output);
                Assert.Equal("quality_json", port.ParameterName);
                Assert.Equal(PortControlType.Download, port.ControlType);
            },
            output =>
            {
                VisionParameter<double> port = Assert.IsType<VisionParameter<double>>(output);
                Assert.Equal("quality_score", port.ParameterName);
            },
            output =>
            {
                VisionParameter<int> port = Assert.IsType<VisionParameter<int>>(output);
                Assert.Equal("corner_count", port.ParameterName);
            },
            output =>
            {
                VisionParameter<bool> port = Assert.IsType<VisionParameter<bool>>(output);
                Assert.Equal("pattern_found", port.ParameterName);
            },
            output =>
            {
                VisionParameter<bool> port = Assert.IsType<VisionParameter<bool>>(output);
                Assert.Equal("is_valid", port.ParameterName);
            }
        );

        Assert.Contains(configs!, config => config.Name == "patternRows");
        Assert.Contains(configs, config => config.Name == "patternCols");
        Assert.Contains(configs, config => config.Name == "minSharpness");
        Assert.Contains(configs, config => config.Name == "minCoverageRatio");
        Assert.Contains(configs, config => config.Name == "drawOverlay");
    }

    [Fact]
    public void Execute_Should_Report_Valid_For_Sharp_Chessboard_Image()
    {
        using Mat input = CreateChessboardImage(
            patternRows: 6,
            patternCols: 9,
            squareSize: 36,
            margin: 24
        );
        using WorkflowContext context = new();
        context.Set("input_mat", input.Clone());

        using var op = new chessboard_quality_assessment(
            patternRows: 6,
            patternCols: 9,
            minSharpness: 5,
            minCoverageRatio: 0.15,
            drawOverlay: true
        );

        op.Execute(context);

        Assert.True(context.Get<bool>("pattern_found"));
        Assert.True(context.Get<bool>("is_valid"));
        Assert.Equal(54, context.Get<int>("corner_count"));
        Assert.True(context.Get<double>("quality_score") > 0.5d);

        string? json = context.Get<string>("quality_json");
        Assert.False(string.IsNullOrWhiteSpace(json));

        using JsonDocument document = JsonDocument.Parse(json!);
        Assert.True(document.RootElement.GetProperty("patternFound").GetBoolean());
        Assert.True(document.RootElement.GetProperty("isValid").GetBoolean());
        Assert.Equal(54, document.RootElement.GetProperty("cornerCount").GetInt32());
        Assert.True(document.RootElement.GetProperty("sharpness").GetDouble() > 0d);
        Assert.True(document.RootElement.GetProperty("coverageRatio").GetDouble() >= 0.15d);

        Mat? output = context.Get<Mat>("output_mat");
        Assert.NotNull(output);
        Assert.False(output!.Empty());
        Assert.Equal(3, output.Channels());
    }

    [Fact]
    public void Execute_Should_Report_Invalid_When_Pattern_Is_Missing()
    {
        using Mat input = new(320, 480, MatType.CV_8UC1, Scalar.All(255));
        using WorkflowContext context = new();
        context.Set("input_mat", input.Clone());

        using var op = new chessboard_quality_assessment(
            patternRows: 6,
            patternCols: 9,
            minSharpness: 5,
            minCoverageRatio: 0.15,
            drawOverlay: true
        );

        op.Execute(context);

        Assert.False(context.Get<bool>("pattern_found"));
        Assert.False(context.Get<bool>("is_valid"));
        Assert.Equal(0, context.Get<int>("corner_count"));
        Assert.Equal(0d, context.Get<double>("quality_score"));
    }

    private static Mat CreateChessboardImage(
        int patternRows,
        int patternCols,
        int squareSize,
        int margin
    )
    {
        int boardRows = patternRows + 1;
        int boardCols = patternCols + 1;
        int width = boardCols * squareSize + margin * 2;
        int height = boardRows * squareSize + margin * 2;

        Mat image = new(height, width, MatType.CV_8UC1, Scalar.All(255));
        for (int row = 0; row < boardRows; row++)
        {
            for (int col = 0; col < boardCols; col++)
            {
                if (((row + col) & 1) != 0)
                    continue;

                Cv2.Rectangle(
                    image,
                    new Rect(
                        margin + col * squareSize,
                        margin + row * squareSize,
                        squareSize,
                        squareSize
                    ),
                    Scalar.All(0),
                    -1
                );
            }
        }

        return image;
    }
}
