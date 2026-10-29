using System.Text.Json;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Application.Integrations;
using Files.Domain.Enums;

namespace Files.Api.Middleware;

/// <summary>Marks an endpoint whose mutating requests honor <c>Idempotency-Key</c> (FR-INT-007).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class IdempotentAttribute : Attribute;

/// <summary>
/// Idempotency for integration mutations (FR-INT-007). Engages only on endpoints carrying
/// <see cref="IdempotentAttribute"/> and only when an <c>Idempotency-Key</c> header is present:
/// the key is scoped to the integration, the request hash binds it to its original body, a matching
/// completed request is replayed, a different body yields 422 <c>idempotency_key_reuse</c>, and an
/// in-flight original yields 409 <c>idempotency_in_progress</c>. 5xx responses are not persisted so
/// a retry with the same key re-runs.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IIdentityService identityService,
        IIdempotencyRepository idempotency,
        TimeProvider timeProvider)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null)
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            await next(context);
            return;
        }

        if (!IdempotencyKeys.IsValid(key))
        {
            await Problems.WriteAsync(context, StatusCodes.Status400BadRequest, "validation_error", "Idempotency-Key must be between 8 and 128 characters");
            return;
        }

        var actor = await identityService.ResolveAsync(context.RequestAborted);
        if (actor.Type != ActorType.Integration || actor.Id is null)
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
            body = buffer.ToArray();
        }

        context.Request.Body.Position = 0;

        var requestHash = IdempotencyKeys.Hash(context.Request.Method, context.Request.Path.Value ?? string.Empty, body);
        var now = timeProvider.GetUtcNow();
        var outcome = await idempotency.BeginAsync(
            "integration", actor.Id.Value, key, requestHash, now, now + IdempotencyKeys.Retention, context.RequestAborted);

        switch (outcome.Kind)
        {
            case IdempotencyOutcomeKind.Replay:
                await WriteReplayAsync(context, outcome);
                return;
            case IdempotencyOutcomeKind.Reuse:
                await Problems.WriteAsync(context, StatusCodes.Status422UnprocessableEntity, "idempotency_key_reuse", "Idempotency-Key was already used with a different request");
                return;
            case IdempotencyOutcomeKind.InProgress:
                context.Response.Headers["Retry-After"] = "1";
                await Problems.WriteAsync(context, StatusCodes.Status409Conflict, "idempotency_in_progress", "The original request is still in progress");
                return;
        }

        var originalBody = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        responseBuffer.Position = 0;
        var responseBytes = responseBuffer.ToArray();

        if (context.Response.StatusCode >= 500)
        {
            // A transient failure must not poison the key; drop the in-progress record so a retry
            // with the same key re-executes.
            await idempotency.AbandonAsync("integration", actor.Id.Value, key, context.RequestAborted);
        }
        else
        {
            await idempotency.CompleteAsync(
                outcome.RecordId!.Value,
                context.Response.StatusCode,
                responseBytes.Length == 0 ? null : responseBytes,
                CaptureHeaders(context.Response),
                now,
                context.RequestAborted);
        }

        if (responseBytes.Length > 0)
        {
            context.Response.ContentLength = responseBytes.Length;
            await originalBody.WriteAsync(responseBytes, context.RequestAborted);
        }
    }

    private static async Task WriteReplayAsync(HttpContext context, IdempotencyOutcome outcome)
    {
        context.Response.StatusCode = outcome.Status ?? StatusCodes.Status200OK;
        ApplyHeaders(context, outcome.Headers);
        if (outcome.Body is { Length: > 0 })
        {
            context.Response.ContentLength = outcome.Body.Length;
            await context.Response.Body.WriteAsync(outcome.Body, context.RequestAborted);
        }
    }

    private static string CaptureHeaders(HttpResponse response)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(response.ContentType))
        {
            headers["Content-Type"] = response.ContentType;
        }

        if (response.Headers.TryGetValue("ETag", out var etag))
        {
            headers["ETag"] = etag.ToString();
        }

        if (response.Headers.TryGetValue("Location", out var location))
        {
            headers["Location"] = location.ToString();
        }

        return JsonSerializer.Serialize(headers);
    }

    private static void ApplyHeaders(HttpContext context, string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
        {
            return;
        }

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
        if (headers is null)
        {
            return;
        }

        if (headers.TryGetValue("Content-Type", out var contentType) && !string.IsNullOrEmpty(contentType))
        {
            context.Response.ContentType = contentType;
        }

        if (headers.TryGetValue("ETag", out var etag) && !string.IsNullOrEmpty(etag))
        {
            context.Response.Headers["ETag"] = etag;
        }

        if (headers.TryGetValue("Location", out var location) && !string.IsNullOrEmpty(location))
        {
            context.Response.Headers["Location"] = location;
        }
    }
}
