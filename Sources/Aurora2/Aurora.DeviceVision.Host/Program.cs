using Aurora.DeviceVision.Host;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
ValidateLoopbackUrls(builder.Configuration["Urls"] ?? "http://127.0.0.1:50052");
builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024;
    options.MaxSendMessageSize = 16 * 1024 * 1024;
});
builder.Services.AddSingleton<SimulatedDeviceStore>();

var app = builder.Build();
app.MapGrpcService<DeviceGatewayService>();
app.MapGrpcService<VisionGatewayService>();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.Run();

static void ValidateLoopbackUrls(string urls)
{
    foreach (var value in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !IPAddress.TryParse(uri.Host, out var address) ||
            !IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException("Aurora Device/Vision Host must remain loopback-only.");
        }
    }
}
