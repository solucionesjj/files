using Files.Application.Abstractions;

namespace Files.Infrastructure.Persistence;

/// <summary>EF Core-backed unit of work over a single <see cref="FilesDbContext"/>.</summary>
public sealed class FilesUnitOfWork(FilesDbContext db) : IUnitOfWork
{
    public Task BeginTransactionAsync(CancellationToken ct = default) =>
        db.Database.BeginTransactionAsync(ct);

    public Task CommitAsync(CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("No active transaction to commit.");
        return transaction.CommitAsync(ct);
    }

    public Task RollbackAsync(CancellationToken ct = default) =>
        db.Database.CurrentTransaction?.RollbackAsync(ct) ?? Task.CompletedTask;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
