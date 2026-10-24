using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Files.Infrastructure.Health;

/// <summary>
/// Readiness probe for PostgreSQL. Runs <c>SELECT 1</c> so <c>/health/ready</c> reflects a real
/// dependency instead of just process liveness.
/// </summary>
public sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck, IDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(
        configuration.GetConnectionString("FilesDb")
        ?? throw new InvalidOperationException("ConnectionStrings__FilesDb is required."));

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = _dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("database is unreachable", ex);
        }
    }

    public void Dispose() => _dataSource.Dispose();
}
