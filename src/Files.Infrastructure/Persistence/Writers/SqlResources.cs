using System.Reflection;

namespace Files.Infrastructure.Persistence.Writers;

/// <summary>Loads embedded <c>.sql</c> resources under <c>Persistence/Sql</c>.</summary>
internal static class SqlResources
{
    private static readonly Assembly Assembly = typeof(SqlResources).Assembly;

    public static string Load(string resourceName)
    {
        var fullName = Assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(resourceName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");

        using var stream = Assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{fullName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
