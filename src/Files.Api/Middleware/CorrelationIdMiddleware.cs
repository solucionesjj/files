using System.Security.Cryptography;

namespace Files.Api.Middleware;

/// <summary>
/// Assigns or echoes a correlation ID so every request can be traced across logs and error
/// responses. The ID is surfaced in the <c>X-Correlation-Id</c> response header and in
/// <see cref="ProblemDetails"/> extensions.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "Files.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
        {
            correlationId = NewCorrelationId();
        }

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }

    public static string? GetCorrelationId(HttpContext? context) =>
        context is not null && context.Items.TryGetValue(ItemKey, out var value)
            ? value as string
            : null;

    private static string NewCorrelationId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
