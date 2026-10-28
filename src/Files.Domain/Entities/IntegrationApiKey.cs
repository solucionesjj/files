namespace Files.Domain.Entities;

/// <summary>
/// Files-issued machine credential for an integration (<c>integration_api_keys</c> table, ADR-0014).
/// Only the SHA-256 hash of the full key is stored; the secret is shown once at creation.
/// </summary>
public sealed class IntegrationApiKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid IntegrationId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Public lookup prefix (<c>fsk_…</c>); never the secret itself.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>SHA-256 of the full key (prefix + "." + secret), 32 bytes.</summary>
    public byte[] SecretHash { get; set; } = [];

    /// <summary>Capabilities for this key, a subset of the integration's permissions.</summary>
    public string[] Scopes { get; set; } = [];

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }
}
