using Files.IntegrationTests.Persistence;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// Upload tests share one PostgreSQL 18 + Flyway fixture and one LocalStack S3 fixture, and are
/// serialized against other collections because they configure the host via process-wide env vars.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UploadCollectionDefinition : ICollectionFixture<PostgresDatabaseFixture>, ICollectionFixture<LocalStackS3Fixture>, ICollectionFixture<ClamAvFixture>
{
    public const string Name = "upload";
}
