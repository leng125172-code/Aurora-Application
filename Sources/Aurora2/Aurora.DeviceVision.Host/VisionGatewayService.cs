using Aurora.V2;
using Grpc.Core;
using System.Text.Json;

namespace Aurora.DeviceVision.Host;

public sealed class VisionGatewayService : VisionGateway.VisionGatewayBase
{
    private const int MaximumInlineFrameBytes = 16 * 1024 * 1024;

    public override Task<ListVisionOperatorsReply> ListOperators(
        ListVisionOperatorsRequest request,
        ServerCallContext context)
    {
        var reply = new ListVisionOperatorsReply();
        reply.Operators.Add(new VisionOperatorDescriptor
        {
            OperatorId = "vision.mean-intensity",
            DisplayName = "Mean intensity",
            Description = "Deterministic GRAY8 min/max/mean measurement used by the 2.0 integration boundary.",
            AcceptedPixelFormats = { "GRAY8" },
            ParametersSchemaJson = "{\"type\":\"object\",\"additionalProperties\":false}"
        });
        reply.Operators.Add(new VisionOperatorDescriptor
        {
            OperatorId = "vision.identity",
            DisplayName = "Identity frame",
            Description = "Returns the input frame unchanged for transport verification.",
            AcceptedPixelFormats = { "GRAY8" },
            ParametersSchemaJson = "{\"type\":\"object\",\"additionalProperties\":false}"
        });
        return Task.FromResult(reply);
    }

    public override Task<VisionOperatorReply> ExecuteOperator(
        ExecuteVisionOperatorRequest request,
        ServerCallContext context)
    {
        var frame = request.InputFrame ?? throw new RpcException(
            new Status(StatusCode.InvalidArgument, "VISION.INPUT_FRAME.REQUIRED"));
        ValidateFrame(frame);
        return request.OperatorId switch
        {
            "vision.mean-intensity" => Task.FromResult(Measure(frame)),
            "vision.identity" => Task.FromResult(new VisionOperatorReply
            {
                Succeeded = true,
                Message = "Identity frame completed",
                MeasurementsJson = "{}",
                OutputFrame = frame.Clone()
            }),
            _ => throw new RpcException(new Status(StatusCode.NotFound, "VISION.OPERATOR.NOT_FOUND"))
        };
    }

    private static VisionOperatorReply Measure(FrameDescriptor frame)
    {
        var pixels = frame.InlineData.Span;
        byte minimum = byte.MaxValue;
        byte maximum = byte.MinValue;
        ulong sum = 0;
        foreach (var pixel in pixels)
        {
            minimum = Math.Min(minimum, pixel);
            maximum = Math.Max(maximum, pixel);
            sum += pixel;
        }
        return new VisionOperatorReply
        {
            Succeeded = true,
            Message = "Mean intensity completed",
            MeasurementsJson = JsonSerializer.Serialize(new
            {
                minimum,
                maximum,
                mean = (double)sum / pixels.Length,
                pixelCount = pixels.Length
            })
        };
    }

    private static void ValidateFrame(FrameDescriptor frame)
    {
        if (frame.Transport is not FrameTransport.Inline)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "VISION.FRAME.TRANSPORT_UNSUPPORTED"));
        }
        if (!string.Equals(frame.PixelFormat, "GRAY8", StringComparison.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "VISION.FRAME.PIXEL_FORMAT_UNSUPPORTED"));
        }
        var expectedLength = checked((ulong)frame.Stride * frame.Height);
        if (frame.Width == 0 || frame.Height == 0 || frame.Stride < frame.Width ||
            expectedLength == 0 || expectedLength > MaximumInlineFrameBytes ||
            frame.ByteLength != expectedLength || (ulong)frame.InlineData.Length != expectedLength)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "VISION.FRAME.INVALID"));
        }
    }
}
