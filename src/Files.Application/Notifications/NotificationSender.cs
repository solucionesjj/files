using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Configuration;
using Microsoft.Extensions.Logging;

namespace Files.Application.Notifications;

/// <summary>One notification-send pass summary (ADR-0009).</summary>
public sealed record NotificationSendSummary(int Reclaimed, int Sent, int Failed, int Suppressed);

/// <summary>Sends due notifications via <see cref="IEmailSender"/> with retry/backoff (ADR-0009).</summary>
public interface INotificationSender
{
    Task<NotificationSendSummary> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Claims due events (deduped by the unique schedule key), renders the recipient-locale template,
/// sends via SMTP, and records delivery attempts. Provider acceptance is distinct from delivery;
/// transient failures retry with backoff, permanent failures are terminal.
/// </summary>
public sealed partial class NotificationSender(
    INotificationEventRepository events,
    IEmailTemplateRepository templates,
    INotificationPolicyRepository policies,
    IEmailSender emailSender,
    IPlatformSettingsRepository settings,
    IUnitOfWork unitOfWork,
    SmtpOptions smtp,
    SchedulerOptions scheduler,
    TimeProvider timeProvider,
    ILogger<NotificationSender> logger) : INotificationSender
{
    private const int BatchSize = 50;

    public async Task<NotificationSendSummary> RunOnceAsync(CancellationToken ct = default)
    {
        if (await settings.GetBoolAsync("notifications_enabled", ct) == false)
        {
            return new NotificationSendSummary(0, 0, 0, 0);
        }

        var now = timeProvider.GetUtcNow();

        var reclaimed = await ReclaimStuckAsync(now, ct);

        var templateKey = (await policies.GetActiveAsync(ct))?.TemplateKey ?? "file.expiring";

        IReadOnlyList<NotificationWorkItem> workItems;
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            workItems = await events.ClaimDueAsync(BatchSize, scheduler.NotificationMaxAttempts, now, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        var sent = 0;
        var failed = 0;
        var suppressed = 0;
        foreach (var item in workItems)
        {
            if (string.IsNullOrWhiteSpace(item.RecipientEmail))
            {
                await MarkFailedAsync(item, permanent: true, "no_recipient", now, ct);
                suppressed++;
                continue;
            }

            var template = await ResolveTemplateAsync(templateKey, smtp.DefaultLocale, ct);
            var rendered = template is null
                ? null
                : EmailTemplateRenderer.Render(template, new EmailVariables(
                    item.FileName, item.ExpirationAt, item.IntervalDays, item.OwnerName,
                    smtp.PortalUrl, item.FileId.ToString("D")));

            if (rendered is null)
            {
                await MarkFailedAsync(item, permanent: true, "template_render_failed", now, ct);
                failed++;
                continue;
            }

            var result = await emailSender.SendAsync(
                new EmailMessage(item.RecipientEmail, rendered.Subject, rendered.TextBody, rendered.HtmlBody),
                ct);

            if (result.Accepted)
            {
                await MarkAcceptedAsync(item, result.ProviderMessageId, now, ct);
                sent++;
                continue;
            }

            var permanent = result.Permanent || item.Attempts >= scheduler.NotificationMaxAttempts;
            await MarkFailedAsync(
                item,
                permanent,
                result.Error ?? "send_failed",
                now,
                ct);
            failed++;

            Log.SendFailed(logger, item.EventId, item.Attempts, permanent);
        }

        return new NotificationSendSummary(reclaimed, sent, failed, suppressed);
    }

    private async Task<int> ReclaimStuckAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddSeconds(-scheduler.SenderReclaimSeconds);
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var reclaimed = await events.ReclaimStuckAsync(cutoff, ct);
            await unitOfWork.CommitAsync(ct);
            return reclaimed;
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private async Task MarkAcceptedAsync(NotificationWorkItem item, string? providerMessageId, DateTimeOffset now, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await events.MarkAcceptedAsync(item.EventId, item.Attempts, providerMessageId, now, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private async Task MarkFailedAsync(
        NotificationWorkItem item,
        bool permanent,
        string error,
        DateTimeOffset now,
        CancellationToken ct)
    {
        DateTimeOffset? nextAttemptAt = permanent ? null : now + NotificationRetry.BackoffDelay(item.Attempts);
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await events.MarkFailedAsync(item.EventId, item.Attempts, permanent, error, nextAttemptAt, now, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<Domain.Entities.EmailTemplate?> ResolveTemplateAsync(string key, string locale, CancellationToken ct)
    {
        var primary = await templates.GetActiveAsync(key, locale, ct);
        if (primary is not null)
        {
            return primary;
        }

        if (!string.Equals(locale, "en-US", StringComparison.Ordinal))
        {
            var english = await templates.GetActiveAsync(key, "en-US", ct);
            if (english is not null)
            {
                return english;
            }
        }

        return await templates.GetNewestActiveAsync(key, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {EventId} attempt {Attempt} failed (permanent: {Permanent})")]
        public static partial void SendFailed(ILogger logger, Guid eventId, int attempt, bool permanent);
    }
}
