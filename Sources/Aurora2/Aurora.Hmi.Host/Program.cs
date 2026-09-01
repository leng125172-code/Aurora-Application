using Aurora.Hmi.Host;
using Aurora.V2;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var coreAddress = builder.Configuration["Aurora:CoreGrpc"] ?? "http://127.0.0.1:50051";
if (!Uri.TryCreate(coreAddress, UriKind.Absolute, out var coreUri) || !coreUri.IsLoopback)
{
    throw new InvalidOperationException("Aurora:CoreGrpc must be an absolute loopback URI.");
}

builder.Services.AddGrpcClient<StationControl.StationControlClient>(options => options.Address = coreUri);
builder.Services.AddGrpcClient<DeviceGateway.DeviceGatewayClient>(options => options.Address = coreUri);
builder.Services.AddGrpcClient<VisionGateway.VisionGatewayClient>(options => options.Address = coreUri);
builder.Services.AddSignalR();
builder.Services.AddSingleton<RemoteAccessFilter>();
builder.Services.AddSingleton<LocalOnlyFilter>();

var remoteRateLimit = Math.Clamp(
    builder.Configuration.GetValue<int?>("Aurora:RemoteRateLimitPerMinute") ?? 30,
    1,
    1_000);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = static async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Remote control rate limit exceeded",
            errorCode = "HMI.REMOTE.RATE_LIMITED"
        }, cancellationToken);
    };
    options.AddPolicy("remote-control", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = remoteRateLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var allowedOrigins = builder.Configuration.GetSection("Aurora:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();
if (allowedOrigins.Length > 0)
{
    app.UseCors();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (StationControl.StationControlClient core, CancellationToken cancellation) =>
{
    try
    {
        var snapshot = await core.GetStationAsync(new GetStationRequest { StationId = "main" }, cancellationToken: cancellation);
        return Results.Ok(new { status = "ready", core = "connected", sequence = snapshot.Sequence });
    }
    catch (Grpc.Core.RpcException error)
    {
        return Results.Problem(statusCode: 503, title: "Rust Core is unavailable", detail: error.Status.Detail);
    }
});

app.MapGet("/api/stations/{stationId}", async (
    string stationId,
    StationControl.StationControlClient core,
    CancellationToken cancellation) =>
    await core.GetStationAsync(new GetStationRequest { StationId = stationId }, cancellationToken: cancellation).ResponseAsync);

app.MapGet("/api/devices", async (
    DeviceGateway.DeviceGatewayClient devices,
    CancellationToken cancellation) =>
    await devices.ListDevicesAsync(new ListDevicesRequest(), cancellationToken: cancellation).ResponseAsync);
app.MapGet("/api/devices/{deviceId}", async (
    string deviceId,
    DeviceGateway.DeviceGatewayClient devices,
    CancellationToken cancellation) =>
    await devices.GetDeviceAsync(
        new GetDeviceRequest { DeviceId = deviceId },
        cancellationToken: cancellation).ResponseAsync);
app.MapGet("/api/vision/operators", async (
    VisionGateway.VisionGatewayClient vision,
    CancellationToken cancellation) =>
    await vision.ListOperatorsAsync(
        new ListVisionOperatorsRequest(),
        cancellationToken: cancellation).ResponseAsync);

var localDevices = app.MapGroup("/api/local/devices").AddEndpointFilter<LocalOnlyFilter>();
localDevices.MapPost("/{deviceId}/capture", async (
    string deviceId,
    CaptureBody body,
    DeviceGateway.DeviceGatewayClient devices,
    CancellationToken cancellation) =>
{
    var frame = await devices.CaptureFrameAsync(new CaptureFrameRequest
    {
        DeviceId = deviceId,
        TimeoutMs = body.TimeoutMs,
        Preview = body.Preview
    }, cancellationToken: cancellation).ResponseAsync;
    return FrameResult(frame);
});
localDevices.MapPost("/{deviceId}/vision/{operatorId}", async (
    string deviceId,
    string operatorId,
    VisionRunBody body,
    DeviceGateway.DeviceGatewayClient devices,
    VisionGateway.VisionGatewayClient vision,
    CancellationToken cancellation) =>
{
    var frame = await devices.CaptureFrameAsync(new CaptureFrameRequest
    {
        DeviceId = deviceId,
        TimeoutMs = body.TimeoutMs,
        Preview = body.Preview
    }, cancellationToken: cancellation).ResponseAsync;
    var reply = await vision.ExecuteOperatorAsync(new ExecuteVisionOperatorRequest
    {
        OperatorId = operatorId,
        InputFrame = frame,
        ParametersJson = body.ParametersJson ?? "{}"
    }, cancellationToken: cancellation).ResponseAsync;
    return Results.Ok(new
    {
        reply.Succeeded,
        reply.ErrorCode,
        reply.Message,
        reply.MeasurementsJson,
        outputFrame = reply.OutputFrame is null ? null : FrameResultValue(reply.OutputFrame)
    });
});

var local = app.MapGroup("/api/local/stations/{stationId}").AddEndpointFilter<LocalOnlyFilter>();
local.MapPost("/acknowledge", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.AcknowledgeSafeStopAsync, stationId, body, ControlOrigin.LocalHmi, hub, cancellation));
local.MapPost("/start", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.StartAsync, stationId, body, ControlOrigin.LocalHmi, hub, cancellation));
local.MapPost("/stop", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.StopAsync, stationId, body, ControlOrigin.LocalHmi, hub, cancellation));
local.MapPost("/emergency-stop", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.EmergencyStopAsync, stationId, body, ControlOrigin.LocalHmi, hub, cancellation));

var remote = app.MapGroup("/api/remote/stations/{stationId}").AddEndpointFilter<RemoteAccessFilter>();
remote.MapPost("/lease", async (string stationId, LeaseBody body, StationControl.StationControlClient core,
    CancellationToken cancellation) =>
    await core.AcquireRemoteLeaseAsync(new AcquireRemoteLeaseRequest
    {
        StationId = stationId,
        OperatorId = body.OperatorId,
        RequestedSeconds = body.RequestedSeconds
    }, cancellationToken: cancellation).ResponseAsync)
    .RequireRateLimiting("remote-control");
remote.MapPost("/lease/release", async (string stationId, ReleaseLeaseBody body,
    StationControl.StationControlClient core, CancellationToken cancellation) =>
    await core.ReleaseRemoteLeaseAsync(new ReleaseRemoteLeaseRequest
    {
        StationId = stationId,
        OperatorId = body.OperatorId,
        LeaseToken = body.LeaseToken
    }, cancellationToken: cancellation).ResponseAsync)
    .RequireRateLimiting("remote-control");
remote.MapPost("/start", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.StartAsync, stationId, body, ControlOrigin.RemoteWeb, hub, cancellation))
    .RequireRateLimiting("remote-control");
remote.MapPost("/stop", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.StopAsync, stationId, body, ControlOrigin.RemoteWeb, hub, cancellation))
    .RequireRateLimiting("remote-control");
remote.MapPost("/emergency-stop", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.EmergencyStopAsync, stationId, body, ControlOrigin.RemoteWeb, hub, cancellation));

app.MapHub<StationHub>("/hubs/station");
app.MapFallbackToFile("index.html");
app.Run();

static async Task<CommandReply> Execute(
    Func<StationCommandRequest, Grpc.Core.Metadata?, DateTime?, CancellationToken, Grpc.Core.AsyncUnaryCall<CommandReply>> command,
    string stationId,
    ControlBody body,
    ControlOrigin origin,
    IHubContext<StationHub> hub,
    CancellationToken cancellation)
{
    var reply = await command(new StationCommandRequest
    {
        StationId = stationId,
        OperatorId = body.OperatorId,
        Origin = origin,
        LeaseToken = body.LeaseToken ?? string.Empty,
        Reason = body.Reason ?? string.Empty
    }, null, null, cancellation).ResponseAsync;
    if (reply.Snapshot is not null)
    {
        await hub.Clients.Group(StationHub.GroupName(stationId))
            .SendAsync("StationChanged", reply.Snapshot, cancellation);
    }
    return reply;
}

static IResult FrameResult(FrameDescriptor frame) => Results.Ok(FrameResultValue(frame));

static object FrameResultValue(FrameDescriptor frame) => new
{
    frame.FrameId,
    frame.Width,
    frame.Height,
    frame.Stride,
    frame.PixelFormat,
    frame.FrameIndex,
    frame.CapturedAtUnixMs,
    transport = frame.Transport.ToString(),
    frame.ByteLength,
    frame.SharedMemoryName,
    inlineDataBase64 = frame.InlineData.IsEmpty ? null : Convert.ToBase64String(frame.InlineData.Span)
};

public sealed record ControlBody(string OperatorId, string? LeaseToken, string? Reason);
public sealed record LeaseBody(string OperatorId, uint RequestedSeconds = 60);
public sealed record ReleaseLeaseBody(string OperatorId, string LeaseToken);
public sealed record CaptureBody(uint TimeoutMs = 3_000, bool Preview = true);
public sealed record VisionRunBody(
    string? ParametersJson = null,
    uint TimeoutMs = 3_000,
    bool Preview = true);
