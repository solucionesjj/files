using System.Data;
using System.Globalization;
using Dapper;
using Files.Application.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Files.Infrastructure.Persistence.Repositories;

/// <summary>Dapper read of the <c>platform_settings</c> JSON override table.</summary>
public sealed class PlatformSettingsRepository(FilesDbContext db) : IPlatformSettingsRepository
{
    private const string ReadSql = "SELECT value::text FROM platform_settings WHERE key = @Key";

    public async Task<int?> GetIntAsync(string key, CancellationToken ct = default)
    {
        var raw = await ReadAsync(key, ct);
        return raw is null || !int.TryParse(Unquote(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? null
            : value;
    }

    public async Task<bool?> GetBoolAsync(string key, CancellationToken ct = default)
    {
        var raw = await ReadAsync(key, ct);
        return raw is null || !bool.TryParse(Unquote(raw), out var value)
            ? null
            : value;
    }

    private async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(ReadSql, new { Key = key }, cancellationToken: ct));
    }

    private static string Unquote(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }
}
