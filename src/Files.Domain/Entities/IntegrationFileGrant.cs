namespace Files.Domain.Entities;

/// <summary>
/// Explicit read-only access to a file owned by another integration
/// (<c>integration_file_grants</c> table). A grant authorizes only <c>file.read</c>; it never
/// grants delete or ownership.
/// </summary>
public sealed class IntegrationFileGrant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    public Guid IntegrationId { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Whether the grant currently authorizes access at <paramref name="now"/>.</summary>
    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
