using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// Integration file access (FR-INT). Ownership/grant reads use EF (ownership predicate plus the
/// <c>integration_file_grants</c> active-row check); download accounting uses Dapper SQL so the
/// counter/event writes are atomic in the ambient transaction.
/// </summary>
public sealed class IntegrationFileRepository(FilesDbContext db, TimeProvider timeProvider) : IIntegrationFileRepository
{
    private static readonly string CompleteSql = SqlResources.Load("Sql.Integration.CompleteDownload.sql");
    private static readonly string EventSql = SqlResources.Load("Sql.Integration.RecordDownloadEvent.sql");

    public async Task<FileEntity?> GetAccessibleAsync(Guid fileId, Guid integrationId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        return await db.Files.AsNoTracking().SingleOrDefaultAsync(f =>
            f.Id == fileId
            && (f.OwnerIntegrationId == integrationId
                || db.IntegrationFileGrants.Any(g =>
                    g.FileId == f.Id && g.IntegrationId == integrationId
                    && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now))), ct);
    }

    public async Task<IReadOnlyList<FileEntity>> ListOwnedAsync(
        Guid integrationId,
        IReadOnlyList<FileStatus>? statuses,
        IntegrationFileCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var query = db.Files.AsNoTracking().Where(f => f.OwnerIntegrationId == integrationId);

        if (statuses is { Count: > 0 })
        {
            query = query.Where(f => statuses.Contains(f.Status));
        }

        if (cursor is not null)
        {
            query = query.Where(f =>
                f.CreatedAt < cursor.CreatedAt
                || (f.CreatedAt == cursor.CreatedAt && f.Id < cursor.Id));
        }

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> HasActiveGrantAsync(Guid fileId, Guid integrationId, DateTimeOffset now, CancellationToken ct = default) =>
        await db.IntegrationFileGrants.AsNoTracking().AnyAsync(g =>
            g.FileId == fileId && g.IntegrationId == integrationId
            && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now), ct);

    public async Task RecordCompletedDownloadAsync(Guid fileId, Guid integrationId, long bytesSent, CancellationToken ct = default)
    {
        var transaction = RequireTransaction();
        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(CompleteSql, new { FileId = fileId, IntegrationId = integrationId, BytesSent = bytesSent }, transaction, cancellationToken: ct));
    }

    public async Task RecordDownloadEventAsync(Guid? fileId, Guid integrationId, string result, string reasonCode, CancellationToken ct = default)
    {
        var transaction = RequireTransaction();
        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(EventSql, new { FileId = fileId, IntegrationId = integrationId, Result = result, ReasonCode = reasonCode }, transaction, cancellationToken: ct));
    }

    private DbTransaction RequireTransaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Integration download writes require an active transaction.");
}
