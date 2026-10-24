using Files.IntegrationTests.Persistence;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// Auth tests get their own PostgreSQL 18 + Flyway fixture and are serialized against other tests
/// because they configure the host via process-wide environment variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuthCollectionDefinition : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "auth";
}
