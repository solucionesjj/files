using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace Files.IntegrationTests.Persistence;

/// <summary>
/// Boots a real PostgreSQL 18 container and applies the canonical Flyway migrations (V1/V2) from an
/// empty database, exactly as production does. Shared across the persistence tests so the database
/// is provisioned once per assembly.
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private const string Database = "files";
    private const string Username = "files";
    private const string Password = "files";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private PostgreSqlContainer? _postgres;
    private IContainer? _flyway;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithDatabase(Database)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();

        await _postgres.StartAsync();

        var migrationsDirectory = Path.Combine(AppContext.BaseDirectory, "migrations");
        _flyway = new ContainerBuilder("flyway/flyway:11")
            .WithNetwork(_network)
            .WithResourceMapping(new DirectoryInfo(migrationsDirectory), "/flyway/sql")
            .WithEnvironment("FLYWAY_URL", $"jdbc:postgresql://postgres:5432/{Database}")
            .WithEnvironment("FLYWAY_USER", Username)
            .WithEnvironment("FLYWAY_PASSWORD", Password)
            .WithEnvironment("FLYWAY_LOCATIONS", "filesystem:/flyway/sql")
            .WithEnvironment("FLYWAY_CONNECT_RETRIES", "10")
            .WithCommand("migrate")
            // Flyway exits after migrating, so we cannot gate on a liveness signal. Start ungated
            // and poll the logs below until the migration completes.
            .WithWaitStrategy(Wait.ForUnixContainer())
            .Build();

        await _flyway.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(60);
        var logs = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            var (stdout, stderr) = await _flyway.GetLogsAsync(
                _flyway.CreatedTime, DateTime.UtcNow, timestampsEnabled: false);
            logs = stdout + stderr;
            if (logs.Contains("Successfully applied", StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(250);
        }

        if (!logs.Contains("Successfully applied", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Flyway did not apply the migrations successfully.\n" + logs);
        }

        ConnectionString = _postgres.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_flyway is not null)
        {
            await _flyway.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        await _network.DisposeAsync();
    }
}
