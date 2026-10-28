using System.Text.Json;
using Files.Api.Authentication;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Events;
using Files.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Files.Api.Endpoints;

/// <summary>
/// Admin API-key management for integrations (ADR-0014). The secret is returned exactly once, at
/// creation; only the SHA-256 hash is stored and the key is listed by prefix/name.
/// </summary>
public static class AdminApiKeyEndpoints
{
    private const string AggregateType = "integration";

    public static void MapAdminApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/admin/integrations/{integrationId:guid}/api-keys", ListAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapPost("/v1/admin/integrations/{integrationId:guid}/api-keys", CreateAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapDelete("/v1/admin/integrations/{integrationId:guid}/api-keys/{keyId:guid}", RevokeAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
    }

    private static async Task<IResult> ListAsync(
        Guid integrationId,
        HttpContext context,
        IIntegrationRepository integrations,
        IIntegrationApiKeyRepository apiKeys,
        CancellationToken ct)
    {
        if (await integrations.GetByIdAsync(integrationId, ct) is null)
        {
            return Problems.Response(context, StatusCodes.Status404NotFound, "integration_not_found", "Integration not found");
        }

        var keys = await apiKeys.ListByIntegrationAsync(integrationId, ct);
        return Results.Ok(keys.Select(k => ToResponse(k, apiKey: null)).ToArray());
    }

    private static async Task<IResult> CreateAsync(
        Guid integrationId,
        CreateApiKeyRequest request,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationRepository integrations,
        IIntegrationApiKeyRepository apiKeys,
        IUnitOfWork unitOfWork,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid name");
        }

        if (!TryParseScopes(request.Scopes, out var scopes, out _))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid scopes");
        }

        var now = timeProvider.GetUtcNow();
        if (request.ExpiresAt is not null && request.ExpiresAt <= now)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "expiresAt must be in the future");
        }

        await unitOfWork.BeginTransactionAsync(ct);
        var integration = await integrations.GetByIdTrackedAsync(integrationId, ct);
        if (integration is null)
        {
            await unitOfWork.RollbackAsync(ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "integration_not_found", "Integration not found");
        }

        var granted = integration.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        if (!scopes.All(granted.Contains))
        {
            await unitOfWork.RollbackAsync(ct);
            return Problems.Response(context, StatusCodes.Status400BadRequest, "scope_not_granted", "Key scopes must be a subset of the integration's permissions");
        }

        var prefix = ApiKeys.GeneratePrefix();
        var secret = ApiKeys.GenerateSecret();
        var fullKey = ApiKeys.Format(prefix, secret);

        var key = new IntegrationApiKey
        {
            IntegrationId = integration.Id,
            Name = request.Name,
            Prefix = prefix,
            SecretHash = ApiKeys.Hash(fullKey),
            Scopes = scopes,
            CreatedBy = actor.Id,
            ExpiresAt = request.ExpiresAt,
        };
        apiKeys.Add(key);

        await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
        {
            ActorUserId = actor.Id,
            ActorRole = string.Join(',', actor.Roles),
            Action = "integration.api_key.created",
            TargetType = AggregateType,
            TargetId = integration.Id,
            Metadata = JsonSerializer.Serialize(new { apiKeyId = key.Id, prefix, name = key.Name, scopes }),
            TraceId = RequestContext.TraceId(context),
            IpHash = RequestContext.IpHash(context),
        }, ct);

        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = "integration.api_key.created",
            AggregateType = AggregateType,
            AggregateId = integration.Id,
            Payload = JsonSerializer.Serialize(new { apiKeyId = key.Id, integrationId = integration.Id }),
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.CommitAsync(ct);

        return Results.Created(
            $"/v1/admin/integrations/{integration.Id}/api-keys/{key.Id}",
            ToResponse(key, apiKey: fullKey));
    }

    private static async Task<IResult> RevokeAsync(
        Guid integrationId,
        Guid keyId,
        [FromBody] JsonElement body,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationApiKeyRepository apiKeys,
        IUnitOfWork unitOfWork,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        var reason = body.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "reason_required", "Revoking an API key requires a reason");
        }

        await unitOfWork.BeginTransactionAsync(ct);
        var key = await apiKeys.GetByIdTrackedAsync(keyId, ct);
        if (key is null || key.IntegrationId != integrationId)
        {
            await unitOfWork.RollbackAsync(ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "api_key_not_found", "API key not found");
        }

        if (key.RevokedAt is not null)
        {
            await unitOfWork.RollbackAsync(ct);
            return Results.Ok(ToResponse(key, apiKey: null));
        }

        key.RevokedAt = timeProvider.GetUtcNow();
        key.RevokedBy = actor.Id;

        await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
        {
            ActorUserId = actor.Id,
            ActorRole = string.Join(',', actor.Roles),
            Action = "integration.api_key.revoked",
            TargetType = AggregateType,
            TargetId = integrationId,
            Reason = reason,
            Metadata = JsonSerializer.Serialize(new { apiKeyId = key.Id, prefix = key.Prefix }),
            TraceId = RequestContext.TraceId(context),
            IpHash = RequestContext.IpHash(context),
        }, ct);

        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = "integration.api_key.revoked",
            AggregateType = AggregateType,
            AggregateId = integrationId,
            Payload = JsonSerializer.Serialize(new { apiKeyId = key.Id, integrationId }),
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.CommitAsync(ct);

        return Results.Ok(ToResponse(key, apiKey: null));
    }

    private static bool TryParseScopes(IReadOnlyList<string>? values, out string[] scopes, out string? error)
    {
        if (values is null || values.Count == 0)
        {
            scopes = [];
            error = "at least one scope is required";
            return false;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!IntegrationPermissions.All.Contains(value))
            {
                scopes = [];
                error = $"unknown scope '{value}'";
                return false;
            }

            set.Add(value);
        }

        scopes = set.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        error = null;
        return true;
    }

    private static ApiKeyResponse ToResponse(IntegrationApiKey key, string? apiKey) => new(
        key.Id,
        key.Name,
        key.Prefix,
        apiKey,
        key.Scopes.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
        key.CreatedAt,
        key.LastUsedAt,
        key.ExpiresAt,
        key.RevokedAt);
}
