namespace Files.Application.Abstractions.Repositories;

/// <summary>
/// Reads admin-editable policy overrides from <c>platform_settings</c> (seeded by V2, amended by
/// admin tooling). Values are stored as JSON (<c>jsonb</c>); readers tolerate both JSON scalars and
/// quoted strings so admins may store either.
/// </summary>
public interface IPlatformSettingsRepository
{
    /// <summary>Returns the setting as an integer, or <c>null</c> when absent or malformed.</summary>
    Task<int?> GetIntAsync(string key, CancellationToken ct = default);

    /// <summary>Returns the setting as a boolean, or <c>null</c> when absent or malformed.</summary>
    Task<bool?> GetBoolAsync(string key, CancellationToken ct = default);
}
