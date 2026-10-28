using System.Data.Common;
using Dapper;
using Files.Application.Abstractions;
using Files.Domain.Enums;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Files.Infrastructure.Persistence.Writers;

/// <summary>
/// Dapper writer for the concurrency-sensitive chunk claim: an idempotent insert plus an atomic
/// increment of the session's received bytes. Mirrors the data model §5 SQL and runs in the ambient
/// unit-of-work transaction.
/// </summary>
public sealed class UploadChunkWriter(FilesDbContext db) : IUploadChunkWriter
{
    private static readonly string InsertSql = SqlResources.Load("Sql.Upload.InsertUploadChunk.sql");
    private static readonly string IncrementSql = SqlResources.Load("Sql.Upload.IncrementUploadSessionReceived.sql");

    public async Task<ChunkWriteResult> WriteChunkAsync(
        Guid sessionId,
        int chunkNumber,
        long offsetBytes,
        long sizeBytes,
        string? checksumSha256,
        CancellationToken ct = default)
    {
        var transaction = RequireTransaction();
        var connection = transaction.Connection!;

        var inserted = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(InsertSql, new
            {
                SessionId = sessionId,
                ChunkNumber = chunkNumber,
                OffsetBytes = offsetBytes,
                SizeBytes = sizeBytes,
                ChecksumSha256 = checksumSha256,
            }, transaction, cancellationToken: ct)).ConfigureAwait(false);

        if (inserted is null)
        {
            return new ChunkWriteResult(false, 0, UploadSessionStatus.Uploading);
        }

        var row = await connection.QuerySingleAsync<IncrementRow>(
            new CommandDefinition(IncrementSql, new
            {
                SessionId = sessionId,
                SizeBytes = sizeBytes,
            }, transaction, cancellationToken: ct)).ConfigureAwait(false);

        var status = Enum.Parse<UploadSessionStatus>(row.Status.ToPascalCase(), ignoreCase: false);
        return new ChunkWriteResult(true, row.ReceivedBytes, status);
    }

    private DbTransaction RequireTransaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("Chunk writes require an active transaction.");

    private sealed record IncrementRow(long ReceivedBytes, string Status);
}
