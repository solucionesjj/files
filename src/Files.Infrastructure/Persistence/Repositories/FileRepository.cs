using Files.Application.Abstractions.Repositories;
using Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

public sealed class FileRepository(FilesDbContext db) : IFileRepository
{
    public void Add(FileEntity file) => db.Files.Add(file);

    public Task<FileEntity?> GetOwnedFileAsync(Guid fileId, Guid ownerUserId, CancellationToken ct = default) =>
        db.Files
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.OwnerUserId == ownerUserId, ct);

    public Task<FileEntity?> GetOwnedIntegrationFileAsync(
        Guid fileId,
        Guid ownerIntegrationId,
        CancellationToken ct = default) =>
        db.Files
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.OwnerIntegrationId == ownerIntegrationId, ct);
}
