using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Shareable download link with a token hash and optional key verifier.</summary>
public sealed class DownloadLink
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public LinkAccessMode AccessMode { get; set; }

    public bool AllowAllTenant { get; set; }

    public string? DownloadKeyHash { get; set; }

    public short? DownloadKeyVersion { get; set; }

    public LinkStatus Status { get; set; } = LinkStatus.Active;

    public DateTimeOffset? ExpiresAt { get; set; }

    public int? MaxDownloads { get; set; }

    public int SuccessfulDownloads { get; set; }

    public int ReservedDownloads { get; set; }

    public LinkCreatorType CreatedByType { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public string? RevokedReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
