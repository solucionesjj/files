namespace Files.Api.Authentication;

/// <summary>
/// Strongly-typed <c>Entra__*</c> configuration (see backend-design §4). No secrets are held here;
/// the API only validates inbound tokens and never stores a client secret.
/// </summary>
public sealed class EntraOptions
{
    public const string SectionName = "Entra";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    public string? TenantId { get; set; }

    public string? ApiClientId { get; set; }

    public string? ApiAudience { get; set; }

    public string? PortalClientId { get; set; }

    public string? RequiredScope { get; set; } = "Files.Access";

    public bool RequireTenantMatch { get; set; } = true;

    public int ClockSkewSeconds { get; set; } = 120;

    /// <summary>
    /// Optional RSA public key (base64) used instead of Entra authority discovery. Intended for
    /// self-signed test JWTs only; never set in production.
    /// </summary>
    public string? IssuerSigningKey { get; set; }
}
