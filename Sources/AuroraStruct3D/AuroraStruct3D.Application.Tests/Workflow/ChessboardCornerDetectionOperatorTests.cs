using System.Text.Json;
using AuroraStruct3D.OpenCV.GeometryOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class ChessboardCornerDetectionOperatorTests
{
    [Fact]
    public void PortDefinitions_Should_Expose_Calibration_Image_And_Result_Ports()
    {
        List<IVisionParameter>? inputs = chessboard_corner_detection.InputVisionParameters;
        List<IVisionParameter>? outputs = chessboard_corner_detection.OutputVisionParameters;
        List<IConfigParameter>? configs = chessboard_corner_detection.ConfigParameters;

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
                Assert.Equal("corners_json", port.ParameterName);
                Assert.Equal(PortControlType.Download, port.ControlType);
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
            }
        );

        Assert.Contains(configs!, config => config.Name == "patternRows");
        Assert.Contains(configs, config => config.Name == "patternCols");
        Assert.Contains(configs, config => config.Name == "refineCorners");
        Assert.Contains(configs, config => config.Name == "drawCorners");
    }

    [Fact]
    public void Execute_Should_Find_Expected_Number_Of_Chessboard_Corners()
    {
        using Mat input = CreateChessboardImage(
            patternRows: 6,
            patternCols: 9,
            squareSize: 36,
            margin: 24
        );
        using WorkflowContext context = new();
        context.Set("input_mat", input.Clone());

        using var op = new chessboard_corner_detection(
            patternRows: 6,
            patternCols: 9,
            refineCorners: true,
            drawCorners: true
        );

        op.Execute(context);

        Assert.True(context.Get<bool>("pattern_found"));
        Assert.Equal(54, context.Get<int>("corner_count"));

        string? json = context.Get<string>("corners_json");
        Assert.False(string.IsNullOrWhiteSpace(json));

        using JsonDocument document = JsonDocument.Parse(json!);
        Assert.True(document.RootElement.GetProperty("patternFound").GetBoolean());
        Assert.Equal(6, document.RootElement.GetProperty("patternRows").GetInt32());
        Assert.Equal(9, document.RootElement.GetProperty("patternCols").GetInt32());
        Assert.Equal(54, document.RootElement.GetProperty("cornerCount").GetInt32());
        Assert.Equal(54, document.RootElement.GetProperty("corners").GetArrayLength());

        Mat? output = context.Get<Mat>("output_mat");
        Assert.NotNull(output);
        Assert.False(output!.Empty());
        Assert.Equal(3, output.Channels());
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
