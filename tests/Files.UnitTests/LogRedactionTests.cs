using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Files.UnitTests;

/// <summary>
/// Secret/log-leakage guard (I-03): no structured log template in the loaded Files assemblies may
/// emit a filename, object/storage key, email, API key, token, password, or grant. The same policy
/// is enforced across the whole source tree by <c>scripts/check-log-leakage.sh</c> and by gitleaks in
/// CI. This test locks the application/infrastructure log templates against regression.
/// </summary>
public class LogRedactionTests
{
    private static readonly string[] BannedPlaceholders =
    [
        "{FileName}", "{Filename}", "{StorageKey}", "{ObjectKey}", "{Email}",
        "{ApiKey}", "{Token}", "{Secret}", "{Password}", "{Authorization}", "{Key}",
    ];

    [Fact]
    public void No_log_message_template_emits_a_sensitive_field()
    {
        var templates = LoadedFilesAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Message)
            .ToList();

        templates.Should().NotBeEmpty("the referenced Files assemblies should declare structured log messages");

        var offenders = templates
            .SelectMany((template, index) => BannedPlaceholders
                .Where(banned => template.Contains(banned, StringComparison.OrdinalIgnoreCase))
                .Select(banned => $"{banned} in \"{template}\""))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty(
            $"log templates must never emit filenames, keys, tokens, emails, or object keys. Offenders: {string.Join("; ", offenders)}");
    }

    private static List<Assembly> LoadedFilesAssemblies() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("Files", StringComparison.Ordinal) == true)];
}
