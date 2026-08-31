using Aurora.Hmi.Host;
using Aurora.V2;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
var coreAddress = builder.Configuration["Aurora:CoreGrpc"] ?? "http://127.0.0.1:50051";
if (!Uri.TryCreate(coreAddress, UriKind.Absolute, out var coreUri) || !coreUri.IsLoopback)
{
    throw new InvalidOperationException("Aurora:CoreGrpc must be an absolute loopback URI.");
}

builder.Services.AddGrpcClient<StationControl.StationControlClient>(options => options.Address = coreUri);
builder.Services.AddSignalR();
builder.Services.AddSingleton<RemoteAccessFilter>();
builder.Services.AddSingleton<LocalOnlyFilter>();

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
    }, cancellationToken: cancellation).ResponseAsync);
remote.MapPost("/lease/release", async (string stationId, ReleaseLeaseBody body,
    StationControl.StationControlClient core, CancellationToken cancellation) =>
    await core.ReleaseRemoteLeaseAsync(new ReleaseRemoteLeaseRequest
    {
        StationId = stationId,
        OperatorId = body.OperatorId,
        LeaseToken = body.LeaseToken
    }, cancellationToken: cancellation).ResponseAsync);
remote.MapPost("/start", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.StartAsync, stationId, body, ControlOrigin.RemoteWeb, hub, cancellation));
remote.MapPost("/stop", (string stationId, ControlBody body, StationControl.StationControlClient core,
    IHubContext<StationHub> hub, CancellationToken cancellation) =>
    Execute(core.StopAsync, stationId, body, ControlOrigin.RemoteWeb, hub, cancellation));
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

public sealed record ControlBody(string OperatorId, string? LeaseToken, string? Reason);
public sealed record LeaseBody(string OperatorId, uint RequestedSeconds = 60);
public sealed record ReleaseLeaseBody(string OperatorId, string LeaseToken);
