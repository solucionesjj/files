using System.Security.Cryptography;
using System.Text;
using Files.Api.Middleware;

namespace Files.Api;

/// <summary>Request-scoped, non-sensitive context used for audit metadata (never raw tokens/paths).</summary>
public static class RequestContext
{
    public static string? TraceId(HttpContext context) => CorrelationIdMiddleware.GetCorrelationId(context);

    /// <summary>SHA-256 of the caller IP (hex), stored in audit rows as <c>ip_hash</c>.</summary>
    public static string? IpHash(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(address));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
