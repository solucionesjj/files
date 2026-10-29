using System.Data;
using System.Data.Common;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Files.Application.Files;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Infrastructure.Persistence.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core + Dapper implementation of <see cref="IDownloadLinkRepository"/>. Reads and simple
/// writes go through EF; the atomic <c>max_downloads</c> reservation, expired-slot release, and
/// revoke-grant invalidation use raw SQL in the ambient transaction (ADR-0005).
/// </summary>
public sealed class DownloadLinkRepository(FilesDbContext db) : IDownloadLinkRepository
{
    private static readonly string ReleaseExpiredSql = SqlResources.Load("Sql.Link.ReleaseExpiredReservations.sql");
    private static readonly string ReserveSlotSql = SqlResources.Load("Sql.Link.ReserveSlot.sql");
    private static readonly string RevokeGrantsSql = SqlResources.Load("Sql.Link.RevokeGrants.sql");

    public void Add(DownloadLink link) => db.DownloadLinks.Add(link);

    public async Task<int> RevokeActiveForFileAsync(
        Guid fileId,
        Guid revokedBy,
        DateTimeOffset revokedAt,
        CancellationToken ct = default)
    {
        var active = await db.DownloadLinks
            .Where(l => l.FileId == fileId && l.Status == LinkStatus.Active)
            .ToListAsync(ct);

        foreach (var link in active)
        {
            link.Status = LinkStatus.Revoked;
            link.RevokedAt = revokedAt;
            link.RevokedBy = revokedBy;
        }

        return active.Count;
    }

    public Task<DownloadLink?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct = default) =>
        db.DownloadLinks.AsNoTracking().SingleOrDefaultAsync(l => l.TokenHash == tokenHash, ct);

    public Task<DownloadLink?> GetOwnedAsync(Guid linkId, Guid ownerUserId, CancellationToken ct = default) =>
        db.DownloadLinks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.Id == linkId && l.CreatedBy == ownerUserId, ct);

    public Task<DownloadLink?> GetOwnedTrackedAsync(Guid linkId, Guid ownerUserId, CancellationToken ct = default) =>
        db.DownloadLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.CreatedBy == ownerUserId, ct);

    public async Task<IReadOnlyList<DownloadLink>> ListForFileAsync(
        Guid fileId,
        LinkCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        IQueryable<DownloadLink> links = db.DownloadLinks
            .AsNoTracking()
            .Where(l => l.FileId == fileId);

        links = OrderNewestFirst(links, cursor);
        return await links.Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DownloadLink>> ListByCreatorAsync(
        Guid creatorUserId,
        LinkStatus? status,
        LinkCursor? cursor,
        int limit,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        IQueryable<DownloadLink> links = db.DownloadLinks
            .AsNoTracking()
            .Where(l => l.CreatedBy == creatorUserId);

        links = status switch
        {
            LinkStatus.Active => links.Where(l => l.Status == LinkStatus.Active
                && (l.ExpiresAt == null || l.ExpiresAt > now)),
            LinkStatus.Revoked => links.Where(l => l.Status == LinkStatus.Revoked),
            LinkStatus.Expired => links.Where(l => l.Status == LinkStatus.Expired
                || (l.Status == LinkStatus.Active && l.ExpiresAt != null && l.ExpiresAt <= now)),
            _ => links,
        };

        links = OrderNewestFirst(links, cursor);
        return await links.Take(limit).ToListAsync(ct);
    }

    public Task<int> CountActiveForFileAsync(Guid fileId, DateTimeOffset now, CancellationToken ct = default) =>
        db.DownloadLinks.CountAsync(l => l.FileId == fileId && l.Status == LinkStatus.Active
            && (l.ExpiresAt == null || l.ExpiresAt > now), ct);

    public Task<int> CountActiveByCreatorAsync(Guid creatorUserId, DateTimeOffset now, CancellationToken ct = default) =>
        db.DownloadLinks.CountAsync(l => l.CreatedBy == creatorUserId && l.Status == LinkStatus.Active
            && (l.ExpiresAt == null || l.ExpiresAt > now), ct);

    public async Task<bool> IsActiveAsync(Guid linkId, DateTimeOffset now, CancellationToken ct = default)
    {
        var link = await db.DownloadLinks.AsNoTracking()
            .Where(l => l.Id == linkId)
            .Select(l => new { l.Status, l.ExpiresAt })
            .SingleOrDefaultAsync(ct);
        return link is not null
            && link.Status == LinkStatus.Active
            && (link.ExpiresAt is null || link.ExpiresAt > now);
    }

    public async Task<bool> TryReserveAsync(Guid linkId, DateTimeOffset now, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Link reservation requires an active transaction.");

        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            ReleaseExpiredSql, new { LinkId = linkId, Now = now }, transaction, cancellationToken: ct));

        var id = await transaction.Connection!.QuerySingleOrDefaultAsync<Guid>(new CommandDefinition(
            ReserveSlotSql, new { LinkId = linkId, Now = now }, transaction, cancellationToken: ct));

        return id != Guid.Empty;
    }

    public async Task<int> RevokeGrantsAsync(Guid linkId, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Grant revocation requires an active transaction.");

        return await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            RevokeGrantsSql, new { LinkId = linkId }, transaction, cancellationToken: ct));
    }

    private const string ListRecentEventsSql =
        """
        SELECT id, file_id AS FileId, link_id AS LinkId, actor_type AS ActorType, actor_id AS ActorId,
               source AS Source, result AS Result, reason_code AS ReasonCode, bytes_sent AS BytesSent,
               trace_id AS TraceId, occurred_at AS OccurredAt
          FROM download_events
         WHERE link_id = @LinkId
         ORDER BY occurred_at DESC
         LIMIT @Limit
        """;

    private const string CountFailedKeyAttemptsSql =
        "SELECT count(*) FROM download_key_attempts WHERE link_id = @LinkId AND outcome = 'failed'";

    private const string LastDownloadAtSql =
        "SELECT max(occurred_at) FROM download_events WHERE link_id = @LinkId AND result = 'completed'";

    public async Task<IReadOnlyList<DownloadEventRecord>> ListRecentEventsAsync(
        Guid linkId,
        int limit,
        CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        var rows = await connection.QueryAsync<DownloadEventRow>(new CommandDefinition(
            ListRecentEventsSql, new { LinkId = linkId, Limit = limit }, cancellationToken: ct));
        return rows
            .Select(r => new DownloadEventRecord(
                r.Id, r.FileId, r.LinkId, r.ActorType, r.ActorId, r.Source, r.Result,
                r.ReasonCode, r.BytesSent, r.TraceId, new DateTimeOffset(r.OccurredAt, TimeSpan.Zero)))
            .ToArray();
    }

    public async Task<int> CountFailedKeyAttemptsAsync(Guid linkId, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            CountFailedKeyAttemptsSql, new { LinkId = linkId }, cancellationToken: ct));
    }

    public async Task<DateTimeOffset?> GetLastDownloadAtAsync(Guid linkId, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        var last = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            LastDownloadAtSql, new { LinkId = linkId }, cancellationToken: ct));
        return last is null ? null : new DateTimeOffset(last.Value, TimeSpan.Zero);
    }

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return connection;
    }

    private static IQueryable<DownloadLink> OrderNewestFirst(IQueryable<DownloadLink> links, LinkCursor? cursor)
    {
        if (cursor is { } c)
        {
            links = links.Where(l => l.CreatedAt < c.CreatedAt || (l.CreatedAt == c.CreatedAt && l.Id < c.Id));
        }

        return links.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id);
    }

    private sealed record DownloadEventRow(
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
        DateTime OccurredAt);
}
