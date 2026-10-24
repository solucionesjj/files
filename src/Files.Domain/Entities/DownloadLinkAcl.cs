using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>A user or group allowed to redeem a private link (<c>download_link_acl</c> table).</summary>
public sealed class DownloadLinkAcl
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid LinkId { get; set; }

    public PrincipalType PrincipalType { get; set; }

    public Guid PrincipalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
