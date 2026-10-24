namespace Files.Api;

/// <summary>Public, non-sensitive configuration served by <c>GET /v1/config</c>.</summary>
public sealed record PublicConfig(
    string Service,
    string Version,
    string Environment,
    FeatureFlags Features,
    PortalInfo Portal,
    I18nInfo I18n,
    RetentionInfo Retention,
    LinksInfo Links);

public sealed record FeatureFlags(bool PublicLinks, bool MalwareScanning, bool Notifications);

public sealed record PortalInfo(string BaseUrl, string SupportEmail);

public sealed record I18nInfo(string DefaultLocale, IReadOnlyList<string> SupportedLocales);

public sealed record RetentionInfo(int DefaultDays, int MaxDays);

public sealed record LinksInfo(int PublicTtlDays, int PrivateTtlDays);
