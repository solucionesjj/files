using Microsoft.Extensions.Configuration;

namespace Files.Application.Configuration;

/// <summary>
/// Link policy, key-derivation, and abuse-control configuration (ADR-0005). The public TTL here is
/// only the *default* fallback: the authoritative lifetime for new links is the admin override
/// <c>platform_settings.public_link_ttl_days</c>, and whether links may be created at all is
/// <c>platform_settings.public_links_enabled</c>. The pepper and abuse thresholds are read from the
/// canonical operator keys (<c>Auth__LinkKeyPepper*</c>, <c>Abuse__*</c>); the pepper is validated
/// (never reported) by the fail-fast <see cref="ConfigurationValidator"/>.
/// </summary>
public sealed record LinksOptions(
    int PublicTtlDays,
    int PrivateTtlDays,
    string LinkKeyPepper,
    int LinkKeyPepperVersion,
    int MaxKeyAttemptsPerWindow,
    int KeyAttemptWindowMinutes,
    int KeyLockoutMinutes,
    int MaxFailuresPerLinkPerHour)
{
    public const int DefaultPublicTtlDays = 15;
    public const int DefaultPrivateTtlDays = 30;
    public const int DefaultLinkKeyPepperVersion = 1;
    public const int DefaultMaxKeyAttemptsPerWindow = 5;
    public const int DefaultKeyAttemptWindowMinutes = 15;
    public const int DefaultKeyLockoutMinutes = 15;
    public const int DefaultMaxFailuresPerLinkPerHour = 100;

    public static LinksOptions FromConfiguration(IConfiguration configuration) => new(
        PublicTtlDays: configuration.GetValue("Links:PublicTtlDays", DefaultPublicTtlDays),
        PrivateTtlDays: configuration.GetValue("Links:PrivateTtlDays", DefaultPrivateTtlDays),
        LinkKeyPepper: configuration["Auth:LinkKeyPepper"] ?? string.Empty,
        LinkKeyPepperVersion: configuration.GetValue("Auth:LinkKeyPepperVersion", DefaultLinkKeyPepperVersion),
        MaxKeyAttemptsPerWindow: configuration.GetValue("Abuse:MaxKeyAttemptsPerWindow", DefaultMaxKeyAttemptsPerWindow),
        KeyAttemptWindowMinutes: configuration.GetValue("Abuse:KeyAttemptWindowMinutes", DefaultKeyAttemptWindowMinutes),
        KeyLockoutMinutes: configuration.GetValue("Abuse:KeyLockoutMinutes", DefaultKeyLockoutMinutes),
        MaxFailuresPerLinkPerHour: configuration.GetValue("Abuse:MaxFailuresPerLinkPerHour", DefaultMaxFailuresPerLinkPerHour));
}
