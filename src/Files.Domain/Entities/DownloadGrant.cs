using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Short-lived capability to stream one file (<c>download_grants</c> table).</summary>
public sealed class DownloadGrant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    public Guid? LinkId { get; set; }

    public byte[] GrantTokenHash { get; set; } = [];

    public GrantSource Source { get; set; }

    public Guid? IssuedToUserId { get; set; }

    public string? IssuedIpHash { get; set; }

    public GrantState State { get; set; } = GrantState.Issued;

    public bool ReservedSlot { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RedeemedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public long BytesSent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
