using System.Security.Cryptography;
using System.Text;

namespace Aurora.Hmi.Host;

/// <summary>Fails closed unless a remote API key was explicitly configured.</summary>
public sealed class RemoteAccessFilter : IEndpointFilter
{
    private readonly byte[][] configuredKeyDigests;

    public RemoteAccessFilter(IConfiguration configuration)
    {
        var configuredKeys = configuration
            .GetSection("Aurora:RemoteApiKeys")
            .Get<string[]>() ?? [];
        var legacyKey = configuration["Aurora:RemoteApiKey"];
        configuredKeyDigests = configuredKeys
            .Append(legacyKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select(HashKey)
            .ToArray();
    }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var remoteAddress = context.HttpContext.Connection.RemoteIpAddress;
        if (!context.HttpContext.Request.IsHttps &&
            (remoteAddress is null || !System.Net.IPAddress.IsLoopback(remoteAddress)))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Remote control requires HTTPS",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "HMI.REMOTE.HTTPS_REQUIRED" }));
        }
        var supplied = context.HttpContext.Request.Headers["X-Aurora-Remote-Key"].ToString();
        var suppliedDigest = HashKey(supplied);
        var authorized = configuredKeyDigests.Aggregate(
            false,
            (matched, configured) => CryptographicOperations.FixedTimeEquals(configured, suppliedDigest) || matched);
        if (!authorized)
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Remote control is disabled or the credential is invalid",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "HMI.REMOTE.UNAUTHORIZED" }));
        }
        return next(context);
    }

    private static byte[] HashKey(string? value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
}

/// <summary>Allows safety-critical local endpoints only from the machine loopback interface.</summary>
public sealed class LocalOnlyFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var address = context.HttpContext.Connection.RemoteIpAddress;
        return address is not null && System.Net.IPAddress.IsLoopback(address)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "This operation requires the local HMI",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "HMI.LOCAL.REQUIRED" }));
    }
}
