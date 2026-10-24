namespace Files.IntegrationTests.Persistence;

/// <summary>Shares the single Postgres + Flyway fixture across all persistence tests.</summary>
[CollectionDefinition(Name)]
public sealed class PersistenceCollectionDefinition : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "persistence";
}
