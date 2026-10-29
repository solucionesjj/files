using Files.IntegrationTests.Persistence;
using Files.IntegrationTests.Upload;

namespace Files.IntegrationTests.Deletion;

/// <summary>
/// Deletion/reconciliation tests share one PostgreSQL 18 + Flyway fixture and one LocalStack S3
/// fixture, and are serialized against other collections because they configure the host via
/// process-wide environment variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DeletionCollectionDefinition : ICollectionFixture<PostgresDatabaseFixture>, ICollectionFixture<LocalStackS3Fixture>
{
    public const string Name = "deletion";
}
