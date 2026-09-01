using Aurora.V2;
using Google.Protobuf;
using Grpc.Core;

namespace Aurora.DeviceVision.Host;

public sealed class SimulatedDeviceStore
{
    private long frameIndex;

    public IReadOnlyList<DeviceDescriptor> Devices { get; } =
    [
        new DeviceDescriptor
        {
            DeviceId = "sim-camera-1",
            DriverId = "aurora.simulator.camera",
            HardwareId = "SIM-CAMERA-0001",
            DisplayName = "Aurora simulated area camera",
            Kind = DeviceKind.Camera,
            State = DeviceConnectionState.Connected,
            Simulated = true,
            ConnectionSummary = "in-process deterministic simulator",
            Capabilities = { "capture.raw", "capture.preview", "trigger.software" }
        },
        new DeviceDescriptor
        {
            DeviceId = "sim-projector-1",
            DriverId = "aurora.simulator.projector",
            HardwareId = "SIM-PROJECTOR-0001",
            DisplayName = "Aurora simulated structured-light projector",
            Kind = DeviceKind.Projector,
            State = DeviceConnectionState.Connected,
            Simulated = true,
            ConnectionSummary = "control contract reserved; actuation not exposed",
            Capabilities = { "pattern.sequence", "trigger.software" }
        },
        new DeviceDescriptor
        {
            DeviceId = "sim-motor-1",
            DriverId = "aurora.simulator.motor",
            HardwareId = "SIM-RS485-1-AXIS-1",
            DisplayName = "Aurora simulated RS485 motor axis",
            Kind = DeviceKind.Motor,
            State = DeviceConnectionState.Connected,
            Simulated = true,
            ConnectionSummary = "control contract reserved; actuation not exposed",
            Capabilities = { "motion.home", "motion.absolute", "motion.emergency-stop" }
        }
    ];

    public FrameDescriptor Capture(string deviceId)
    {
        const int width = 64;
        const int height = 48;
        var data = new byte[width * height];
        var index = Interlocked.Increment(ref frameIndex);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                data[(y * width) + x] = (byte)((x + y + index) & 0xff);
            }
        }
        return new FrameDescriptor
        {
            FrameId = Guid.NewGuid().ToString("N"),
            Width = width,
            Height = height,
            Stride = width,
            PixelFormat = "GRAY8",
            FrameIndex = checked((ulong)index),
            CapturedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Transport = FrameTransport.Inline,
            InlineData = ByteString.CopyFrom(data),
            ByteLength = (ulong)data.Length
        };
    }
}

public sealed class DeviceGatewayService(SimulatedDeviceStore store)
    : DeviceGateway.DeviceGatewayBase
{
    public override Task<ListDevicesReply> ListDevices(ListDevicesRequest request, ServerCallContext context)
    {
        var reply = new ListDevicesReply();
        reply.Devices.AddRange(store.Devices
            .Where(device => request.Kind is DeviceKind.Unspecified || device.Kind == request.Kind)
            .Select(device => device.Clone()));
        return Task.FromResult(reply);
    }

    public override Task<DeviceDescriptor> GetDevice(GetDeviceRequest request, ServerCallContext context)
    {
        var device = store.Devices.FirstOrDefault(
            candidate => string.Equals(candidate.DeviceId, request.DeviceId, StringComparison.Ordinal));
        return device is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "DEVICE.NOT_FOUND"))
            : Task.FromResult(device.Clone());
    }

    public override Task<FrameDescriptor> CaptureFrame(CaptureFrameRequest request, ServerCallContext context)
    {
        if (request.TimeoutMs is 0 or > 30_000)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "DEVICE.CAPTURE.TIMEOUT_INVALID"));
        }
        var device = store.Devices.FirstOrDefault(
            candidate => string.Equals(candidate.DeviceId, request.DeviceId, StringComparison.Ordinal));
        if (device is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "DEVICE.NOT_FOUND"));
        }
        if (device.Kind is not DeviceKind.Camera)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "DEVICE.CAPTURE.NOT_CAMERA"));
        }
        return Task.FromResult(store.Capture(request.DeviceId));
    }
}
