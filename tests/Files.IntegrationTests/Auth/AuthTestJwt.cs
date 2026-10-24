using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Files.Domain.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// Mints self-signed RS256 JWTs for the API's test signing key (<c>Entra__IssuerSigningKey</c>),
/// so the integration tests exercise the real validation pipeline (signature, issuer, audience,
/// lifetime, tenant, token type) without Entra.
/// </summary>
public static class AuthTestJwt
{
    public static readonly Guid TenantId = Guid.Parse("4c6d5a5f-0000-4000-8000-000000000001");
    public static readonly Guid ApiClientId = Guid.Parse("4c6d5a5f-0000-4000-8000-000000000002");
    public const string Audience = "api://files-tests";

    private static readonly RSA Rsa = RSA.Create(2048);

    public static string PublicKeyBase64 => Convert.ToBase64String(Rsa.ExportSubjectPublicKeyInfo());

    public static string Issuer => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public static string CreateUserToken(
        Guid objectId,
        string? name = null,
        string? email = null,
        params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("tid", TenantId.ToString()),
            new("oid", objectId.ToString()),
            new("name", name ?? "Test User"),
            new("email", email ?? "test@example.com"),
            new("scp", "Files.Access"),
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        return Create(claims);
    }

    public static string CreateAdminToken(Guid objectId) =>
        CreateUserToken(objectId, name: "Files Admin", roles: [PortalRoles.Admin]);

    public static string CreateAppToken(Guid clientId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("tid", TenantId.ToString()),
            new("azp", clientId.ToString()),
            new("idtyp", "app"),
            new("oid", Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        return Create(claims);
    }

    public static ClaimsPrincipal AppPrincipal(Guid clientId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("tid", TenantId.ToString()),
            new("azp", clientId.ToString()),
            new("idtyp", "app"),
            new("oid", Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "BearerIntegration"));
    }

    private static string Create(IEnumerable<Claim> claims)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(new RsaSecurityKey(Rsa), SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: credentials);
        return handler.WriteToken(token);
    }
}
