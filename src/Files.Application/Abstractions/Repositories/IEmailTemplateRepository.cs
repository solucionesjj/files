using Files.Domain.Entities;

namespace Files.Application.Abstractions.Repositories;

/// <summary>Versioned email templates keyed by key + locale, single active per key+locale (ADR-0009).</summary>
public interface IEmailTemplateRepository
{
    /// <summary>All active templates (one per key+locale), newest first.</summary>
    Task<IReadOnlyList<EmailTemplate>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>The active template for a key+locale, or <c>null</c>.</summary>
    Task<EmailTemplate?> GetActiveAsync(string key, string locale, CancellationToken ct = default);

    /// <summary>Tracked active template used to supersede when publishing a new version.</summary>
    Task<EmailTemplate?> GetActiveTrackedAsync(string key, string locale, CancellationToken ct = default);

    /// <summary>The newest active template for a key regardless of locale (last-resort fallback).</summary>
    Task<EmailTemplate?> GetNewestActiveAsync(string key, CancellationToken ct = default);

    void Add(EmailTemplate emailTemplate);
}
