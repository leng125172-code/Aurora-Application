using System.Security.Cryptography;
using System.Text;

namespace Aurora.Hmi.Host;

/// <summary>Fails closed unless a remote API key was explicitly configured.</summary>
public sealed class RemoteAccessFilter(IConfiguration configuration) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = configuration["Aurora:RemoteApiKey"];
        var supplied = context.HttpContext.Request.Headers["X-Aurora-Remote-Key"].ToString();
        if (string.IsNullOrWhiteSpace(configured) || !FixedTimeEquals(configured, supplied))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Remote control is disabled or the credential is invalid",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "HMI.REMOTE.UNAUTHORIZED" }));
        }
        return next(context);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
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
