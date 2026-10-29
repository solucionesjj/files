using System.Text.Json;
using System.Text.RegularExpressions;
using Files.Api.Authentication;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Events;

namespace Files.Api.Endpoints;

/// <summary>
/// Admin notification administration (contract-first, ADR-0009): versioned notification policy and
/// email templates (single active per key+locale). Every mutation is audited + outbox.
/// </summary>
public static partial class AdminNotificationEndpoints
{
    private const string PolicyTarget = "notification_policy";
    private const string TemplateTarget = "email_template";

    public static void MapAdminNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/admin/notification-policy", GetNotificationPolicyAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
        endpoints.MapPut("/v1/admin/notification-policy", UpdateNotificationPolicyAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapGet("/v1/admin/email-templates", ListEmailTemplatesAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
        endpoints.MapPut("/v1/admin/email-templates/{key}/{locale}", UpsertEmailTemplateAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
    }

    private static async Task<IResult> GetNotificationPolicyAsync(
        HttpContext context,
        INotificationPolicyRepository policies,
        CancellationToken ct)
    {
        var policy = await policies.GetActiveAsync(ct);
        return policy is null
            ? Problems.Response(context, StatusCodes.Status404NotFound, "notification_policy_not_found", "No active notification policy")
            : Results.Ok(ToPolicyResponse(policy));
    }

    private static async Task<IResult> UpdateNotificationPolicyAsync(
        UpdateNotificationPolicyRequest request,
        HttpContext context,
        IIdentityService identityService,
        INotificationPolicyRepository policies,
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

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "name must be between 1 and 120 characters");
        }

        if (request.IntervalsDays is not { Length: >= 1 and <= 10 }
            || request.IntervalsDays.Any(d => d is < 1 or > 365)
            || request.IntervalsDays.Distinct().Count() != request.IntervalsDays.Length)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "intervalsDays must be 1..10 distinct values between 1 and 365");
        }

        if (string.IsNullOrWhiteSpace(request.TemplateKey))
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "templateKey is required");
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

                await unitOfWork.SaveChangesAsync(ct);
            }

            var policy = new NotificationPolicy
            {
                Name = request.Name.Trim(),
                IntervalsDays = request.IntervalsDays.OrderByDescending(d => d).ToArray(),
                TemplateKey = request.TemplateKey,
                IsActive = true,
                EffectiveFrom = now,
                CreatedBy = actor.Id,
            };
            policies.Add(policy);

            await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
            {
                ActorUserId = actor.Id,
                ActorRole = string.Join(',', actor.Roles),
                Action = "notification_policy.published",
                TargetType = PolicyTarget,
                TargetId = policy.Id,
                Reason = request.Reason,
                Metadata = JsonSerializer.Serialize(new { intervalsDays = policy.IntervalsDays, templateKey = policy.TemplateKey }),
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await outboxWriter.WriteAsync(new OutboxMessage
            {
                EventType = "notification_policy.published",
                AggregateType = PolicyTarget,
                AggregateId = policy.Id,
                Payload = JsonSerializer.Serialize(new { policyId = policy.Id }),
            }, ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);

            return Results.Ok(ToPolicyResponse(policy));
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<IResult> ListEmailTemplatesAsync(
        HttpContext context,
        IEmailTemplateRepository templates,
        CancellationToken ct)
    {
        var active = await templates.ListActiveAsync(ct);
        return Results.Ok(new EmailTemplateListResponse(active.Select(ToTemplateResponse).ToArray()));
    }

    private static async Task<IResult> UpsertEmailTemplateAsync(
        string key,
        string locale,
        UpsertEmailTemplateRequest request,
        HttpContext context,
        IIdentityService identityService,
        IEmailTemplateRepository templates,
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

        if (!KeyPattern().IsMatch(key))
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "key must match ^[a-z0-9._-]{3,80}$");
        }

        if (!LocalePattern().IsMatch(locale))
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "locale must match ^[a-z]{2}-[A-Z]{2}$");
        }

        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 300)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "subject must be between 1 and 300 characters");
        }

        if (string.IsNullOrWhiteSpace(request.BodyHtml) || string.IsNullOrWhiteSpace(request.BodyText))
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "validation_error", "bodyHtml and bodyText are required");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
        {
            return Problems.Response(context, StatusCodes.Status422UnprocessableEntity, "reason_required", "A reason of at least 10 characters is required");
        }

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var active = await templates.GetActiveTrackedAsync(key, locale, ct);
            var nextVersion = active?.Version + 1 ?? 1;
            if (active is not null)
            {
                active.IsActive = false;
                await unitOfWork.SaveChangesAsync(ct);
            }

            var template = new EmailTemplate
            {
                Key = key,
                Locale = locale,
                Subject = request.Subject,
                BodyHtml = request.BodyHtml,
                BodyText = request.BodyText,
                Version = nextVersion,
                IsActive = true,
                CreatedBy = actor.Id,
            };
            templates.Add(template);

            await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
            {
                ActorUserId = actor.Id,
                ActorRole = string.Join(',', actor.Roles),
                Action = "email_template.updated",
                TargetType = TemplateTarget,
                TargetId = template.Id,
                Reason = request.Reason,
                Metadata = JsonSerializer.Serialize(new { key, locale, version = nextVersion }),
                TraceId = RequestContext.TraceId(context),
                IpHash = RequestContext.IpHash(context),
            }, ct);

            await outboxWriter.WriteAsync(new OutboxMessage
            {
                EventType = "email_template.updated",
                AggregateType = TemplateTarget,
                AggregateId = template.Id,
                Payload = JsonSerializer.Serialize(new { key, locale, version = nextVersion }),
            }, ct);

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitAsync(ct);

            return Results.Ok(ToTemplateResponse(template));
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static NotificationPolicyResponse ToPolicyResponse(NotificationPolicy policy) => new(
        policy.Id,
        policy.Name,
        policy.IntervalsDays,
        policy.TemplateKey,
        policy.IsActive,
        policy.EffectiveFrom);

    private static EmailTemplateResponse ToTemplateResponse(EmailTemplate template) => new(
        template.Id,
        template.Key,
        template.Locale,
        template.Subject,
        template.BodyHtml,
        template.BodyText,
        template.Version,
        template.IsActive,
        template.CreatedAt);

    [GeneratedRegex("^[a-z0-9._-]{3,80}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-z]{2}-[A-Z]{2}$")]
    private static partial Regex LocalePattern();
}

public sealed record UpdateNotificationPolicyRequest(
    string Name,
    int[] IntervalsDays,
    string TemplateKey,
    string Reason);

public sealed record NotificationPolicyResponse(
    Guid Id,
    string Name,
    int[] IntervalsDays,
    string TemplateKey,
    bool IsActive,
    DateTimeOffset EffectiveFrom);

public sealed record UpsertEmailTemplateRequest(
    string Subject,
    string BodyHtml,
    string BodyText,
    string Reason);

public sealed record EmailTemplateResponse(
    Guid Id,
    string Key,
    string Locale,
    string Subject,
    string BodyHtml,
    string BodyText,
    int Version,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record EmailTemplateListResponse(IReadOnlyList<EmailTemplateResponse> Items);
