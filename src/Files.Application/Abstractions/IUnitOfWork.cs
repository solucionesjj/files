namespace Files.Application.Abstractions;

/// <summary>
/// Transactional unit of work. Wraps a single <c>DbContext</c> per request/use case: the caller
/// opens a transaction, mutates entities and writes outbox/audit rows, then calls
/// <see cref="SaveChangesAsync"/> once at the end before committing.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task BeginTransactionAsync(CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);

    Task RollbackAsync(CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
