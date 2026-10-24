using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Persistence.Writers;
using FluentAssertions;

namespace Files.IntegrationTests.Persistence;

/// <summary>
/// Proves the transactional contract: entity changes, outbox messages, and audit rows all live in
/// the same database transaction, so a rollback discards every one of them.
/// </summary>
[Collection(PersistenceCollectionDefinition.Name)]
public class OutboxAuditTransactionTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public OutboxAuditTransactionTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Outbox_audit_and_entity_commit_together()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var unitOfWork = new FilesUnitOfWork(db);
        var outbox = new OutboxWriter(db);
        var audit = new AuditWriter(db);

        var auditAction = "user.created." + Guid.NewGuid().ToString("N");

        await unitOfWork.BeginTransactionAsync();

        var user = User.Create(Guid.NewGuid(), Guid.NewGuid(), "Transactional User");
        db.Users.Add(user);

        await outbox.WriteAsync(new OutboxMessage
        {
            EventType = "user.created",
            AggregateType = "user",
            AggregateId = user.Id,
            Payload = "{}",
        });

        await audit.WriteFileAuditAsync(new AuditEvent
        {
            ActorType = ActorType.System,
            Action = auditAction,
            Result = AuditResult.Success,
        });

        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();

        // Assert by unique keys so the shared database does not skew the counts.
        (await PersistenceTestHelpers.CountWhereAsync(_fixture.ConnectionString, "users", "id", user.Id))
            .Should().Be(1);
        (await PersistenceTestHelpers.CountWhereAsync(_fixture.ConnectionString, "outbox_messages", "aggregate_id", user.Id))
            .Should().Be(1);
        (await PersistenceTestHelpers.CountWhereAsync(_fixture.ConnectionString, "file_audit_events", "action", auditAction))
            .Should().Be(1);
    }

    [Fact]
    public async Task Rollback_discards_outbox_audit_and_entity_changes_together()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var unitOfWork = new FilesUnitOfWork(db);
        var outbox = new OutboxWriter(db);
        var audit = new AuditWriter(db);

        var auditAction = "user.created." + Guid.NewGuid().ToString("N");

        await unitOfWork.BeginTransactionAsync();

        var user = User.Create(Guid.NewGuid(), Guid.NewGuid(), "Rolled Back User");
        db.Users.Add(user);

        await outbox.WriteAsync(new OutboxMessage
        {
            EventType = "user.created",
            AggregateType = "user",
            AggregateId = user.Id,
            Payload = "{}",
        });

        await audit.WriteFileAuditAsync(new AuditEvent
        {
            ActorType = ActorType.System,
            Action = auditAction,
            Result = AuditResult.Success,
        });

        await unitOfWork.SaveChangesAsync();
        await unitOfWork.RollbackAsync();

        (await PersistenceTestHelpers.CountWhereAsync(_fixture.ConnectionString, "users", "id", user.Id))
            .Should().Be(0);
        (await PersistenceTestHelpers.CountWhereAsync(_fixture.ConnectionString, "outbox_messages", "aggregate_id", user.Id))
            .Should().Be(0);
        (await PersistenceTestHelpers.CountWhereAsync(_fixture.ConnectionString, "file_audit_events", "action", auditAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task Writers_fail_fast_without_an_active_transaction()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));
        var outbox = new OutboxWriter(db);

        var act = () => outbox.WriteAsync(new OutboxMessage
        {
            EventType = "user.created",
            AggregateType = "user",
            AggregateId = Guid.NewGuid(),
            Payload = "{}",
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active transaction*");
    }
}
