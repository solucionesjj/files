using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Files.Api.Authentication;

/// <summary>
/// Authenticates an integration from a Files-issued API key in the <c>X-Api-Key</c> header
/// (ADR-0014). The key is looked up by its public prefix and verified in constant time against the
/// stored SHA-256 hash; the handler then checks integration status, expiry, revocation, and the IP
/// allowlist, and emits scope claims (key scopes ∩ integration permissions) for authorization.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IIntegrationApiKeyRepository apiKeys,
    IIntegrationRepository integrations,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    public const string HeaderName = "X-Api-Key";

    private static readonly TimeSpan LastUsedInterval = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var key = values[0];
        if (string.IsNullOrWhiteSpace(key))
        {
            return AuthenticateResult.NoResult();
        }

        var prefix = ApiKeys.TryGetPrefix(key);
        if (prefix is null)
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        var apiKey = await apiKeys.GetByPrefixAsync(prefix, Context.RequestAborted);
        if (apiKey is null || !ApiKeys.Verify(key, apiKey.SecretHash))
        {
            return AuthenticateResult.Fail("Unknown or invalid API key.");
        }

        var now = timeProvider.GetUtcNow();
        if (apiKey.RevokedAt is not null || (apiKey.ExpiresAt is not null && apiKey.ExpiresAt <= now))
        {
            return AuthenticateResult.Fail("API key is revoked or expired.");
        }

        var integration = await integrations.GetByIdAsync(apiKey.IntegrationId, Context.RequestAborted);
        if (integration is null || integration.Status != IntegrationStatus.Active)
        {
            return AuthenticateResult.Fail("Integration is inactive.");
        }

        if (!IsAllowed(integration, Context.Connection.RemoteIpAddress))
        {
            return AuthenticateResult.Fail("Client IP is not allowed.");
        }

        // Effective permissions = key scopes ∩ current integration permissions (least privilege).
        var granted = integration.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        var scopes = apiKey.Scopes.Where(granted.Contains).ToArray();

        await apiKeys.UpdateLastUsedAsync(apiKey.Id, now - LastUsedInterval, now, Context.RequestAborted);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.Id.ToString()),
            new(ClaimTypes.Name, integration.Slug),
            new(ApiKeyClaims.IntegrationId, integration.Id.ToString()),
        };
        claims.AddRange(scopes.Select(scope => new Claim(ApiKeyClaims.Scope, scope)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(Context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid or missing API key");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(Context, StatusCodes.Status403Forbidden, "insufficient_permission", "The integration lacks the required permission");

    private static bool IsAllowed(Integration integration, IPAddress? remote)
    {
        if (integration.AllowedIpCidrs.Length == 0)
        {
            return true;
        }

        return remote is not null && integration.AllowedIpCidrs.Any(network => network.Contains(remote));
    }
}
