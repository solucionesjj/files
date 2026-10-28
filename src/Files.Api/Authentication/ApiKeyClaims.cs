namespace Files.Api.Authentication;

/// <summary>Claim types set by <see cref="ApiKeyAuthenticationHandler"/> on integration principals.</summary>
public static class ApiKeyClaims
{
    public const string IntegrationId = "integration_id";

    public const string Scope = "scope";
}
