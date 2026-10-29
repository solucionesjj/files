using Files.IntegrationTests.Persistence;

namespace Files.IntegrationTests.RetentionApi;

/// <summary>
/// Retention/notification tests share one PostgreSQL 18 + Flyway fixture and one Mailpit SMTP sink,
/// and are serialized against other collections because they configure the host via env vars.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RetentionCollectionDefinition : ICollectionFixture<PostgresDatabaseFixture>, ICollectionFixture<MailpitFixture>
{
    public const string Name = "retention";
}
