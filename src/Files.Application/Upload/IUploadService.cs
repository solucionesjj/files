using Files.Application.Abstractions;

namespace Files.Application.Upload;

/// <summary>
/// Orchestrates the API-proxied upload flow (ADR-0003): session creation, chunk staging with disk
/// reserve checks, resume status, server-side verification + S3 commit, and abort. Ownership is
/// checked on every operation from the caller-supplied owner id (portal user or integration).
/// </summary>
public interface IUploadService
{
    Task<ServiceResult<UploadSessionCreated>> CreatePortalAsync(
        CreatePortalUpload command,
        Guid ownerUserId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);

    Task<ServiceResult<UploadSessionCreated>> CreateIntegrationAsync(
        CreateIntegrationUpload command,
        Guid ownerIntegrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);

    Task<ServiceResult<ChunkAccepted>> WriteChunkAsync(
        Guid sessionId,
        int chunkNumber,
        Stream body,
        long contentLength,
        string? checksumSha256,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);

    Task<ServiceResult<UploadStatusView>> GetStatusAsync(
        Guid sessionId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        CancellationToken ct = default);

    Task<ServiceResult<CompletedUpload>> CompleteAsync(
        Guid sessionId,
        string? checksumSha256,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        CancellationToken ct = default);

    Task<ServiceResult<bool>> AbortAsync(
        Guid sessionId,
        Guid? ownerUserId,
        Guid? ownerIntegrationId,
        string? traceId,
        string? ipHash,
        CancellationToken ct = default);
}
