using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Files.Api.Authentication;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Files.Domain.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Files.Api.Endpoints;

/// <summary>
/// Admin-only integration CRUD (contract-first from OpenAPI <c>/admin/integrations</c>). Every
/// mutation writes an admin-audit row and a transactional outbox message; no API keys or secrets
/// are issued or stored (ADR-0004).
/// </summary>
public static class AdminIntegrationEndpoints
{
    private const string IntegrationTarget = "integration";
    private const string AggregateType = "integration";

    public static void MapAdminIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/admin/integrations", ListAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapPost("/v1/admin/integrations", CreateAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapGet("/v1/admin/integrations/{integrationId:guid}", GetAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapPatch("/v1/admin/integrations/{integrationId:guid}", UpdateAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapPut("/v1/admin/integrations/{integrationId:guid}/permissions", SetPermissionsAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IIntegrationRepository integrations,
        string? status,
        string? cursor,
        int? limit,
        CancellationToken ct)
    {
        IntegrationStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "active", StringComparison.Ordinal))
            {
                statusFilter = IntegrationStatus.Active;
            }
            else if (string.Equals(status, "disabled", StringComparison.Ordinal))
            {
                statusFilter = IntegrationStatus.Disabled;
            }
            else
            {
                return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "Invalid status");
            }
        }

        var pageLimit = limit ?? 25;
        if (pageLimit is < 1 or > 100)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "limit must be between 1 and 100");
        }

        DateTimeOffset? cursorCreatedAt;
        try
        {
            cursorCreatedAt = DecodeCursor(cursor);
        }
        catch (FormatException)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_cursor", "Invalid cursor");
        }

        var rows = await integrations.ListAsync(statusFilter, pageLimit + 1, cursorCreatedAt, ct);
        var hasMore = rows.Count > pageLimit;
        var page = rows.Take(pageLimit).ToList();
        var nextCursor = hasMore && page.Count > 0 ? EncodeCursor(page[^1].CreatedAt) : null;

        return Results.Ok(new IntegrationListResponse(
            page.Select(IntegrationMapper.ToResponse).ToArray(),
            nextCursor,
            hasMore));
    }

    private static async Task<IResult> CreateAsync(
        CreateIntegrationRequest request,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationRepository integrations,
        IUnitOfWork unitOfWork,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!IsValidSlug(request.Slug))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid slug");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid name");
        }

        if (!TryParsePermissions(request.Permissions, out var permissions, out _))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid permissions");
        }

        if (!TryParseCidrs(request.AllowedIpCidrs, out var cidrs, out _))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid allowedIpCidrs");
        }

        if (await integrations.GetBySlugAsync(request.Slug, ct) is not null)
        {
            return Problems.Response(context, StatusCodes.Status409Conflict, "integration_slug_taken", "Slug is already in use");
        }

        var integration = Integration.Create(request.Slug, request.Name, request.Description);
        integration.AllowedIpCidrs = cidrs;
        integration.CreatedBy = actor.Id;
        foreach (var permission in permissions)
        {
            integration.Permissions.Add(new IntegrationPermissionEntry { IntegrationId = integration.Id, Permission = permission });
        }

        await unitOfWork.BeginTransactionAsync(ct);
        integrations.Add(integration);

        await WriteAdminAuditAsync(
            auditWriter,
            actor,
            integration,
            "integration.created",
            reason: null,
            before: null,
            after: integration,
            context,
            ct);

        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = "integration.created",
            AggregateType = AggregateType,
            AggregateId = integration.Id,
            Payload = JsonSerializer.Serialize(new { integrationId = integration.Id, slug = integration.Slug }),
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.CommitAsync(ct);

        return Results.Created($"/v1/admin/integrations/{integration.Id}", IntegrationMapper.ToResponse(integration));
    }

    private static async Task<IResult> GetAsync(
        Guid integrationId,
        HttpContext context,
        IIntegrationRepository integrations,
        CancellationToken ct)
    {
        var integration = await integrations.GetByIdAsync(integrationId, ct);
        return integration is null
            ? Problems.Response(context, StatusCodes.Status404NotFound, "integration_not_found", "Integration not found")
            : Results.Ok(IntegrationMapper.ToResponse(integration));
    }

    private static async Task<IResult> UpdateAsync(
        Guid integrationId,
        JsonElement body,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationRepository integrations,
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

        await unitOfWork.BeginTransactionAsync(ct);
        var integration = await integrations.GetByIdTrackedAsync(integrationId, ct);
        if (integration is null)
        {
            await unitOfWork.RollbackAsync(ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "integration_not_found", "Integration not found");
        }

        var before = IntegrationMapper.ToResponse(integration);
        var reason = ReadNullableString(body, "reason");

        if (body.TryGetProperty("name", out var nameElement))
        {
            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            {
                await unitOfWork.RollbackAsync(ct);
                return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid name");
            }

            integration.Name = name;
        }

        if (body.TryGetProperty("description", out var descriptionElement))
        {
            integration.Description = descriptionElement.ValueKind == JsonValueKind.Null
                ? null
                : descriptionElement.GetString();
        }

        if (body.TryGetProperty("allowedIpCidrs", out var cidrsElement))
        {
            if (!TryParseCidrs(cidrsElement, out var cidrs, out _))
            {
                await unitOfWork.RollbackAsync(ct);
                return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid allowedIpCidrs");
            }

            integration.AllowedIpCidrs = cidrs;
        }

        var disabling = false;
        if (body.TryGetProperty("status", out var statusElement))
        {
            var status = statusElement.GetString();
            if (string.Equals(status, "disabled", StringComparison.Ordinal))
            {
                disabling = true;
            }
            else if (string.Equals(status, "active", StringComparison.Ordinal))
            {
                integration.Status = IntegrationStatus.Active;
                integration.DisabledAt = null;
                integration.DisabledBy = null;
                integration.DisabledReason = null;
            }
            else
            {
                await unitOfWork.RollbackAsync(ct);
                return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid status");
            }
        }

        if (disabling)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
            {
                await unitOfWork.RollbackAsync(ct);
                return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "reason_required", "Disabling an integration requires a reason");
            }

            integration.Status = IntegrationStatus.Disabled;
            integration.DisabledAt = timeProvider.GetUtcNow();
            integration.DisabledBy = actor.Id;
            integration.DisabledReason = reason;
        }

        var action = disabling ? "integration.disabled" : "integration.updated";
        await WriteAdminAuditAsync(
            auditWriter,
            actor,
            integration,
            action,
            reason,
            before,
            integration,
            context,
            ct);

        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = action,
            AggregateType = AggregateType,
            AggregateId = integration.Id,
            Payload = JsonSerializer.Serialize(new { integrationId = integration.Id, status = StatusName(integration.Status) }),
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.CommitAsync(ct);

        return Results.Ok(IntegrationMapper.ToResponse(integration));
    }

    private static async Task<IResult> SetPermissionsAsync(
        Guid integrationId,
        SetPermissionsRequest request,
        HttpContext context,
        IIdentityService identityService,
        IIntegrationRepository integrations,
        IUnitOfWork unitOfWork,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!TryParsePermissions(request.Permissions, out var permissions, out _))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "Invalid permissions");
        }

        await unitOfWork.BeginTransactionAsync(ct);
        var integration = await integrations.GetByIdTrackedAsync(integrationId, ct);
        if (integration is null)
        {
            await unitOfWork.RollbackAsync(ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "integration_not_found", "Integration not found");
        }

        var before = IntegrationMapper.ToResponse(integration);

        integration.Permissions.Clear();
        foreach (var permission in permissions)
        {
            integration.Permissions.Add(new IntegrationPermissionEntry { IntegrationId = integration.Id, Permission = permission });
        }

        await WriteAdminAuditAsync(
            auditWriter,
            actor,
            integration,
            "integration.permissions.updated",
            reason: null,
            before,
            integration,
            context,
            ct);

        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = "integration.permissions.updated",
            AggregateType = AggregateType,
            AggregateId = integration.Id,
            Payload = JsonSerializer.Serialize(new { integrationId = integration.Id }),
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.CommitAsync(ct);

        return Results.Ok(IntegrationMapper.ToResponse(integration));
    }

    private static Task WriteAdminAuditAsync(
        IAuditWriter auditWriter,
        ResolvedActor actor,
        Integration integration,
        string action,
        string? reason,
        IntegrationResponse? before,
        Integration after,
        HttpContext context,
        CancellationToken ct)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            before = before,
            after = IntegrationMapper.ToResponse(after),
        });

        return auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
        {
            ActorUserId = actor.Id,
            ActorRole = string.Join(',', actor.Roles),
            Action = action,
            TargetType = IntegrationTarget,
            TargetId = integration.Id,
            Reason = reason,
            Metadata = metadata,
            TraceId = RequestContext.TraceId(context),
            IpHash = RequestContext.IpHash(context),
        }, ct);
    }

    private static bool TryParsePermissions(IReadOnlyList<string>? values, out IReadOnlyList<string> permissions, out string? error)
    {
        if (values is null || values.Count == 0)
        {
            permissions = [];
            error = "at least one permission is required";
            return false;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!IntegrationPermissions.All.Contains(value))
            {
                permissions = [];
                error = $"unknown permission '{value}'";
                return false;
            }

            set.Add(value);
        }

        permissions = set.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        error = null;
        return true;
    }

    private static bool TryParseCidrs(IReadOnlyList<string>? values, out IPNetwork[] cidrs, out string? error)
    {
        if (values is null || values.Count == 0)
        {
            cidrs = [];
            error = null;
            return true;
        }

        var parsed = new List<IPNetwork>(values.Count);
        foreach (var value in values)
        {
            if (!IPNetwork.TryParse(value, out var network))
            {
                cidrs = [];
                error = $"invalid CIDR '{value}'";
                return false;
            }

            parsed.Add(network);
        }

        cidrs = [.. parsed];
        error = null;
        return true;
    }

    private static bool TryParseCidrs(JsonElement element, out IPNetwork[] cidrs, out string? error)
    {
        if (element.ValueKind is not JsonValueKind.Array)
        {
            cidrs = [];
            error = "allowedIpCidrs must be an array";
            return false;
        }

        return TryParseCidrs(element.EnumerateArray().Select(e => e.GetString()).ToArray()!, out cidrs, out error);
    }

    private static bool IsValidSlug(string? slug)
    {
        if (slug is null || slug.Length is < 3 or > 64)
        {
            return false;
        }

        if (slug[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return false;
        }

        foreach (var c in slug)
        {
            if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadNullableString(JsonElement body, string property)
    {
        if (!body.TryGetProperty(property, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.Null ? null : element.GetString();
    }

    private static string StatusName(IntegrationStatus status) =>
        status == IntegrationStatus.Active ? "active" : "disabled";

    private static string EncodeCursor(DateTimeOffset createdAt) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(createdAt.ToString("O", CultureInfo.InvariantCulture)));

    private static DateTimeOffset? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
