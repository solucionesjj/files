using Files.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Files.IntegrationTests.Persistence;

/// <summary>
/// Validates the EF model against the real PostgreSQL 18 schema (not against a mental image of
/// it): every mapped aggregate table and column must exist in <c>information_schema</c>.
/// </summary>
[Collection(PersistenceCollectionDefinition.Name)]
public class FilesDbContextMappingTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public FilesDbContextMappingTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_mapped_table_and_column_exists_in_the_real_database()
    {
        await using var db = new FilesDbContext(PersistenceTestHelpers.CreateOptions(_fixture.ConnectionString));

        var mapped = db.Model.GetEntityTypes()
            .Select(e => new
            {
                Table = e.GetTableName()!,
                Columns = e.GetProperties().Select(p => p.GetColumnName()).ToArray(),
            })
            .OrderBy(x => x.Table)
            .ToList();

        // The DbContext maps the 12 aggregate tables plus the integration_permissions join table
        // (part of the integration aggregate); operational tables (outbox, idempotency, usage,
        // admin audit, ...) are accessed with Dapper, not EF.
        mapped.Should().HaveCount(13);

        var missing = new List<string>();
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        foreach (var entity in mapped)
        {
            foreach (var column in entity.Columns)
            {
                var exists = await ColumnExistsAsync(connection, entity.Table, column);
                if (!exists)
                {
                    missing.Add($"{entity.Table}.{column}");
                }
            }
        }

        missing.Should().BeEmpty(
            "these mapped tables/columns must exist in the Flyway schema:\n  "
            + string.Join("\n  ", missing));
    }

    private static async Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection,
        string table,
        string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
              FROM information_schema.columns
             WHERE table_schema = 'public'
               AND table_name = @table
               AND column_name = @column
            """;
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return await command.ExecuteScalarAsync() is not null;
    }
}
