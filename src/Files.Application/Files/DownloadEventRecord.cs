namespace Files.Application.Files;

/// <summary>A recent download event (OpenAPI <c>DownloadEvent</c>), materialized from <c>download_events</c>.</summary>
public sealed record DownloadEventRecord(
    Guid Id,
    Guid? FileId,
    Guid? LinkId,
    string ActorType,
    Guid? ActorId,
    string Source,
    string Result,
    string? ReasonCode,
    long BytesSent,
    string? TraceId,
    DateTimeOffset OccurredAt);
