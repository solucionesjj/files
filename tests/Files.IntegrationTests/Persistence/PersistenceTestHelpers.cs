using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Files.IntegrationTests.Persistence;

internal static class PersistenceTestHelpers
{
    public static DbContextOptions<FilesDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<FilesDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    public static async Task<long> CountAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<long> CountWhereAsync(
        string connectionString,
        string table,
        string column,
        object value)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table} WHERE {column} = @value";
        command.Parameters.AddWithValue("value", value);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
