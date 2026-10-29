using System.Text.Json;
using Files.Api.Authentication;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Files;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Microsoft.Extensions.Configuration;

namespace Files.Api.Endpoints;

/// <summary>
/// Admin retention administration (contract-first, ADR-0006): policy versioning, bulk migrations,
/// per-file retention override, and legal hold. Every mutation is audited (admin + file) and writes
/// a transactional outbox message. No file content is ever returned to an admin.
/// </summary>
public static class AdminRetentionEndpoints
{
    private const string PolicyTarget = "retention_policy";
    private const string MigrationTarget = "retention_migration";
    private const string FileTarget = "file";
    private const string FileAggregateType = "file";

    public static void MapAdminRetentionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/admin/retention-policy", GetPolicyAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
        endpoints.MapPost("/v1/admin/retention-policy", CreatePolicyAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapPost("/v1/admin/retention-migrations", CreateMigrationAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
        endpoints.MapGet("/v1/admin/retention-migrations/{migrationId:guid}", GetMigrationAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapPost("/v1/admin/files/{fileId:guid}/retention", OverrideFileRetentionAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
        endpoints.MapPost("/v1/admin/files/{fileId:guid}/legal-hold", SetLegalHoldAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
    }

    private static async Task<IResult> GetPolicyAsync(
        HttpContext context,
        IRetentionPolicyRepository policies,
        CancellationToken ct)
    {
        var all = await policies.ListAllAsync(ct);
        var current = all.FirstOrDefault(p => p.IsActive);
        return Results.Ok(new RetentionPolicyHistoryResponse(
            current is null ? null : ToPolicyResponse(current),
            all.Where(p => !p.IsActive).Select(ToPolicyResponse).ToArray()));
    }

    private static async Task<IResult> CreatePolicyAsync(
        CreateRetentionPolicyRequest request,
        HttpContext context,
        IIdentityService identityService,
        IRetentionPolicyRepository policies,
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

        if (request.DefaultDays is < 1 or > 365 || request.MaxDays is < 1 or > 365 || request.MaxDays < request.DefaultDays)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "defaultDays and maxDays must be within 1..365 and maxDays must be >= defaultDays");
        }

        if (request.AllowIndefinite)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "retention_indefinite_not_allowed", "Indefinite retention is only allowed for integrations, never for human files");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "reason_required", "A reason of at least 10 characters is required");
        }

        var now = timeProvider.GetUtcNow();

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var active = await policies.GetActiveAsync(ct);
            if (active is not null)
            {
                var tracked = await policies.GetByIdTrackedAsync(active.Id, ct);
                if (tracked is not null)
                {
                    tracked.IsActive = false;
                }

                await unitOfWork.SaveChangesAsync(ct); // flush the deactivate before inserting the new active row
            }

            var policy = new RetentionPolicy
            {
                DefaultDays = request.DefaultDays,
                MaxDays = request.MaxDays,
                AllowUserOverride = request.AllowUserOverride,
                AllowIndefinite = false,
                PublicLinksEnabled = request.PublicLinksEnabled,
                IsActive = true,
                EffectiveFrom = now,
                Notes = request.Notes,
                CreatedBy = actor.Id,
            };
            policies.Add(policy);

            await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
            {
                ActorUserId = actor.Id,
                ActorRole = string.Join(',', actor.Roles),
                Action = "retention_policy.published",
                TargetType = PolicyTarget,
                TargetId = policy.Id,
                Reason = request.Reason,
                Metadata = JsonSerializer.Serialize(new { before = active is null ? null : ToPolicyResponse(active), after = ToPolicyResponse(policy) }),
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await outboxWriter.WriteAsync(new OutboxMessage
            {
                EventType = "retention_policy.published",
                AggregateType = PolicyTarget,
                AggregateId = policy.Id,
                Payload = JsonSerializer.Serialize(new { policyId = policy.Id, defaultDays = policy.DefaultDays }),
            }, ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);

            return Results.Created($"/v1/admin/retention-policy/{policy.Id}", ToPolicyResponse(policy));
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<IResult> CreateMigrationAsync(
        CreateRetentionMigrationRequest request,
        HttpContext context,
        IIdentityService identityService,
        IRetentionMigrationRepository migrations,
        IRetentionPolicyRepository policies,
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

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "reason_required", "A reason of at least 10 characters is required");
        }

        var target = await policies.GetByIdAsync(request.TargetPolicyId, ct);
        if (target is null)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "retention_policy_not_found", "Target retention policy not found");
        }

        var migration = new RetentionMigration
        {
            TargetPolicyId = target.Id,
            Scope = request.Scope is null ? "{}" : JsonSerializer.Serialize(request.Scope),
            Reason = request.Reason.Trim(),
            RequestedBy = actor.Id!.Value,
        };

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            migrations.Add(migration);

            await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
            {
                ActorUserId = actor.Id,
                ActorRole = string.Join(',', actor.Roles),
                Action = "retention_migration.requested",
                TargetType = MigrationTarget,
                TargetId = migration.Id,
                Reason = migration.Reason,
                Metadata = JsonSerializer.Serialize(new { targetPolicyId = target.Id }),
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await outboxWriter.WriteAsync(new OutboxMessage
            {
                EventType = "retention_migration.requested",
                AggregateType = MigrationTarget,
                AggregateId = migration.Id,
                Payload = JsonSerializer.Serialize(new { migrationId = migration.Id, targetPolicyId = target.Id }),
            }, ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        return Results.Accepted($"/v1/admin/retention-migrations/{migration.Id}", ToMigrationResponse(migration));
    }

    private static async Task<IResult> GetMigrationAsync(
        Guid migrationId,
        HttpContext context,
        IRetentionMigrationRepository migrations,
        CancellationToken ct)
    {
        var migration = await migrations.GetByIdAsync(migrationId, ct);
        return migration is null
            ? Problems.Response(context, StatusCodes.Status404NotFound, "retention_migration_not_found", "Retention migration not found")
            : Results.Ok(ToMigrationResponse(migration));
    }

    private static async Task<IResult> OverrideFileRetentionAsync(
        Guid fileId,
        AdminRetentionOverrideRequest request,
        HttpContext context,
        IIdentityService identityService,
        IFileRepository files,
        IRetentionPolicyRepository policies,
        INotificationPolicyRepository notificationPolicies,
        INotificationEventRepository notifications,
        ITagRepository tags,
        IUnitOfWork unitOfWork,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        TimeProvider timeProvider,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!string.Equals(request.Mode, "finite", StringComparison.Ordinal))
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "retention_not_allowed", "Only finite retention overrides are allowed");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "reason_required", "A reason of at least 10 characters is required");
        }

        var activePolicy = await policies.GetActiveAsync(ct);
        var defaultDays = activePolicy?.DefaultDays ?? configuration.GetValue("Retention:DefaultDays", RetentionRules.DefaultDays);
        var maxDays = activePolicy?.MaxDays ?? configuration.GetValue("Retention:MaxDays", RetentionRules.MaxDays);
        var days = request.Days ?? defaultDays;
        if (days is < 1 || days > maxDays)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "retention_exceeds_maximum", $"Retention must be between 1 and {maxDays} days");
        }

        var now = timeProvider.GetUtcNow();

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var file = await files.GetByIdTrackedAsync(fileId, ct);
            if (file is null)
            {
                await unitOfWork.RollbackAsync(ct);
                return Problems.Response(context, StatusCodes.Status404NotFound, "file_not_found", "File not found");
            }

            if (file.LegalHold)
            {
                await unitOfWork.RollbackAsync(ct);
                return Problems.Response(context, StatusCodes.Status409Conflict, "legal_hold_active", "Legal hold blocks retention changes");
            }

            var before = FileMetadataMapper.ToResponse(file, await tags.ListFileTagsAsync(fileId, ct));

            file.ApplyRetention(Domain.ValueObjects.Retention.Finite(now.AddDays(days), days));
            file.RetentionReason = request.Reason.Trim();
            file.RetentionPolicyId = activePolicy?.Id;

            await notifications.SupersedeForFileAsync(fileId, ct);
            var notificationPolicy = await notificationPolicies.GetActiveAsync(ct);
            if (file.OwnerType == OwnerType.Human
                && notificationPolicy is not null
                && file.ExpiresAt is not null
                && Files.Application.Notifications.NotificationTiming.HasAnyFutureDue(file.ExpiresAt.Value, notificationPolicy.IntervalsDays, now))
            {
                await notifications.ScheduleForFileAsync(fileId, notificationPolicy.Id, notificationPolicy.IntervalsDays, ct);
            }

            await auditWriter.WriteFileAuditAsync(new AuditEvent
            {
                FileId = fileId,
                ActorType = ActorType.Admin,
                ActorId = actor.Id,
                Action = "retention_changed",
                Result = AuditResult.Success,
                Reason = request.Reason.Trim(),
                Metadata = JsonSerializer.Serialize(new { before = before.Retention, after = FileMetadataMapper.ToResponse(file, []).Retention }),
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
            {
                ActorUserId = actor.Id,
                ActorRole = string.Join(',', actor.Roles),
                Action = "file.retention.overridden",
                TargetType = FileTarget,
                TargetId = fileId,
                Reason = request.Reason.Trim(),
                Metadata = JsonSerializer.Serialize(new { retentionDays = days }),
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await outboxWriter.WriteAsync(new OutboxMessage
            {
                EventType = "file.retention_changed",
                AggregateType = FileAggregateType,
                AggregateId = fileId,
                Payload = JsonSerializer.Serialize(new { fileId }),
            }, ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);

            return Results.Ok(FileMetadataMapper.ToResponse(file, await tags.ListFileTagsAsync(fileId, ct)));
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<IResult> SetLegalHoldAsync(
        Guid fileId,
        LegalHoldRequest request,
        HttpContext context,
        IIdentityService identityService,
        IFileRepository files,
        INotificationEventRepository notifications,
        ITagRepository tags,
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

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "reason_required", "A reason of at least 10 characters is required");
        }

        var now = timeProvider.GetUtcNow();

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var file = await files.GetByIdTrackedAsync(fileId, ct);
            if (file is null)
            {
                await unitOfWork.RollbackAsync(ct);
                return Problems.Response(context, StatusCodes.Status404NotFound, "file_not_found", "File not found");
            }

            var action = request.Enabled ? "legal_hold_set" : "legal_hold_released";

            if (request.Enabled)
            {
                file.LegalHold = true;
                file.LegalHoldReason = request.Reason.Trim();
                file.LegalHoldSetAt = now;
                file.LegalHoldSetBy = actor.Id;
                file.LegalHoldReviewAt = request.ReviewAt;

                await notifications.SupersedeForFileAsync(fileId, ct);
            }
            else
            {
                file.LegalHold = false;
                file.LegalHoldReason = null;
                file.LegalHoldSetAt = null;
                file.LegalHoldSetBy = null;
                file.LegalHoldReviewAt = null;
            }

            await auditWriter.WriteFileAuditAsync(new AuditEvent
            {
                FileId = fileId,
                ActorType = ActorType.Admin,
                ActorId = actor.Id,
                Action = action,
                Result = AuditResult.Success,
                Reason = request.Reason.Trim(),
                Metadata = "{}",
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
            {
                ActorUserId = actor.Id,
                ActorRole = string.Join(',', actor.Roles),
                Action = action,
                TargetType = FileTarget,
                TargetId = fileId,
                Reason = request.Reason.Trim(),
                Metadata = "{}",
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await outboxWriter.WriteAsync(new OutboxMessage
            {
                EventType = action,
                AggregateType = FileAggregateType,
                AggregateId = fileId,
                Payload = JsonSerializer.Serialize(new { fileId, enabled = request.Enabled }),
            }, ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);

            return Results.Ok(FileMetadataMapper.ToResponse(file, await tags.ListFileTagsAsync(fileId, ct)));
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static RetentionPolicyResponse ToPolicyResponse(RetentionPolicy policy) => new(
        policy.Id,
        policy.DefaultDays,
        policy.MaxDays,
        policy.AllowUserOverride,
        policy.AllowIndefinite,
        policy.PublicLinksEnabled,
        policy.IsActive,
        policy.EffectiveFrom,
        policy.Notes,
        policy.CreatedAt);

    private static RetentionMigrationResponse ToMigrationResponse(RetentionMigration migration) => new(
        migration.Id,
        StatusName(migration.Status),
        migration.TargetPolicyId,
        ParseScope(migration.Scope),
        migration.TotalFiles,
        migration.ProcessedFiles,
        migration.FailedFiles,
        migration.LastError,
        migration.CreatedAt,
        migration.StartedAt,
        migration.CompletedAt);

    private static string StatusName(RetentionMigrationStatus status) => status.ToString().ToLowerInvariant();

    private static JsonElement ParseScope(string scope)
    {
        try
        {
            using var doc = JsonDocument.Parse(scope);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}

public sealed record CreateRetentionPolicyRequest(
    int DefaultDays,
    int MaxDays,
    bool AllowUserOverride,
    bool AllowIndefinite,
    bool PublicLinksEnabled,
    string? Notes,
    string Reason);

public sealed record RetentionPolicyResponse(
    Guid Id,
    int DefaultDays,
    int MaxDays,
    bool AllowUserOverride,
    bool AllowIndefinite,
    bool PublicLinksEnabled,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record RetentionPolicyHistoryResponse(
    RetentionPolicyResponse? Current,
    IReadOnlyList<RetentionPolicyResponse> History);

public sealed record CreateRetentionMigrationRequest(
    Guid TargetPolicyId,
    JsonElement? Scope,
    string Reason);

public sealed record RetentionMigrationResponse(
    Guid Id,
    string Status,
    Guid TargetPolicyId,
    JsonElement Scope,
    long TotalFiles,
    long ProcessedFiles,
    long FailedFiles,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record AdminRetentionOverrideRequest(string Mode, int? Days, string Reason);

public sealed record LegalHoldRequest(bool Enabled, string Reason, DateTimeOffset? ReviewAt);
