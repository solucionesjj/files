using Microsoft.Extensions.Configuration;

namespace Files.Application.Configuration;

/// <summary>
/// Outbound SMTP configuration (ADR-0009). <c>AppPassword</c> is the only secret and is read from
/// the environment (<c>Smtp__AppPassword</c>, falling back to <c>Smtp__Password</c>); it must never
/// be logged, returned, or serialized. POP is receive-only and is never used for sending.
/// </summary>
public sealed record SmtpOptions(
    string Host,
    int Port,
    string? Username,
    string? AppPassword,
    bool UseStartTls,
    string FromAddress,
    string FromDisplayName,
    string PortalUrl,
    string DefaultLocale)
{
    public const int DefaultPort = 587;
    public const string FallbackLocale = "es-CO";

    public bool HasCredentials => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(AppPassword);

    public static SmtpOptions FromConfiguration(IConfiguration configuration)
    {
        var host = configuration["Smtp:Host"] ?? string.Empty;
        var password = configuration["Smtp:AppPassword"] ?? configuration["Smtp:Password"];

        return new SmtpOptions(
            Host: host,
            Port: configuration.GetValue("Smtp:Port", DefaultPort),
            Username: NullIfBlank(configuration["Smtp:Username"]),
            AppPassword: NullIfBlank(password),
            UseStartTls: configuration.GetValue("Smtp:UseStartTls", true),
            FromAddress: configuration["Notifications:FromAddress"] ?? "files@localhost",
            FromDisplayName: configuration["Notifications:FromDisplayName"] ?? "Files",
            PortalUrl: configuration["Portal:BaseUrl"] ?? "http://localhost:4200",
            DefaultLocale: configuration["Notifications:DefaultLocale"] ?? FallbackLocale);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
