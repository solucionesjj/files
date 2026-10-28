using System.Security.Cryptography;
using Files.Api.Authorization;
using Files.Application.Identity;
using Files.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Files.Api.Authentication;

/// <summary>
/// Wires Entra ID JWT validation (ADR-0004) as two schemes — <c>BearerUser</c> (delegated human
/// tokens) and <c>BearerIntegration</c> (app-only client-credentials tokens) — plus the portal and
/// integration authorization policies. Validation checks signature, issuer, audience, lifetime,
/// tenant (<c>tid</c>), and token type; machine permissions are enforced later from the database.
/// </summary>
public static class AuthenticationSetup
{
    public const string BearerUser = "BearerUser";
    public const string BearerIntegration = "BearerIntegration";

    public const string PortalPolicy = "Portal";
    public const string UserPolicy = "User";
    public const string AdminPolicy = "Admin";
    public const string AuditorPolicy = "Auditor";
    public const string IntegrationPolicy = "Integration";

    /// <summary>Machine caller must hold the <c>file.write</c> scope (upload operations).</summary>
    public const string IntegrationWritePolicy = "IntegrationWrite";

    private const string AuthFailureCodeKey = "Files.Auth.FailureCode";
    private const string FailureTenantNotAllowed = "tenant_not_allowed";

    public static IServiceCollection AddFilesAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var entra = new EntraOptions();
        configuration.GetSection(EntraOptions.SectionName).Bind(entra);

        var tokenValidationParameters = BuildTokenValidationParameters(entra);

        services
            .AddAuthentication()
            .AddJwtBearer(BearerUser, options =>
                ConfigureScheme(options, tokenValidationParameters, entra))
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                null);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(PortalPolicy, policy => policy
                .AddAuthenticationSchemes(BearerUser)
                .RequireAuthenticatedUser());

            options.AddPolicy(UserPolicy, policy => policy
                .AddAuthenticationSchemes(BearerUser)
                .AddRequirements(new PortalRoleRequirement(PortalRoles.User)));

            options.AddPolicy(AdminPolicy, policy => policy
                .AddAuthenticationSchemes(BearerUser)
                .AddRequirements(new PortalRoleRequirement(PortalRoles.Admin)));

            options.AddPolicy(AuditorPolicy, policy => policy
                .AddAuthenticationSchemes(BearerUser)
                .AddRequirements(new PortalRoleRequirement(PortalRoles.Auditor)));

            options.AddPolicy(IntegrationPolicy, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .AddRequirements(new IntegrationRequirement()));

            options.AddPolicy(IntegrationWritePolicy, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .AddRequirements(new IntegrationRequirement(IntegrationPermissions.FileWrite)));
        });

        services.AddScoped<IAuthorizationHandler, PortalRoleRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, IntegrationRequirementHandler>();

        return services;
    }

    private static void ConfigureScheme(
        JwtBearerOptions options,
        TokenValidationParameters tokenValidationParameters,
        EntraOptions entra)
    {
        options.TokenValidationParameters = tokenValidationParameters;
        options.MapInboundClaims = false;

        // Real Entra: discover signing keys from the tenant authority. The test IssuerSigningKey
        // path below bypasses authority discovery entirely.
        if (string.IsNullOrWhiteSpace(entra.IssuerSigningKey) && !string.IsNullOrWhiteSpace(BuildIssuer(entra)))
        {
            options.Authority = BuildIssuer(entra);
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var principal = context.Principal!;

                if (TokenClaims.IsAppOnly(principal))
                {
                    return Fail(context, "App-only tokens are not valid on user endpoints.");
                }

                if (!string.IsNullOrWhiteSpace(entra.RequiredScope)
                    && !TokenClaims.HasScope(principal, entra.RequiredScope))
                {
                    return Fail(context, "The token is missing the required delegated scope.");
                }

                if (entra.RequireTenantMatch && !TenantMatches(principal, entra.TenantId))
                {
                    context.HttpContext.Items[AuthFailureCodeKey] = FailureTenantNotAllowed;
                    return Fail(context, "Token tenant does not match the configured tenant.");
                }

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                context.HandleResponse();

                var code = ResolveChallengeCode(context);
                var title = code switch
                {
                    FailureTenantNotAllowed => "Tenant not allowed",
                    "token_expired" => "Token expired",
                    _ => "Invalid token",
                };

                return Problems.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, code, title);
            },
            OnForbidden = context =>
                Problems.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "forbidden", "Forbidden"),
        };
    }

    private static string ResolveChallengeCode(JwtBearerChallengeContext context)
    {
        if (context.AuthenticateFailure is SecurityTokenExpiredException)
        {
            return "token_expired";
        }

        if (context.HttpContext.Items.TryGetValue(AuthFailureCodeKey, out var value) && value is string code)
        {
            return code;
        }

        return "invalid_token";
    }

    private static Task Fail(TokenValidatedContext context, string reason)
    {
        context.Fail(reason);
        return Task.CompletedTask;
    }

    private static bool TenantMatches(System.Security.Claims.ClaimsPrincipal principal, string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId)
        || string.Equals(TokenClaims.GetTenantId(principal)?.ToString("D"), tenantId, StringComparison.OrdinalIgnoreCase);

    private static string? BuildIssuer(EntraOptions entra)
    {
        if (string.IsNullOrWhiteSpace(entra.TenantId))
        {
            return null;
        }

        return entra.Instance.TrimEnd('/') + "/" + entra.TenantId.Trim() + "/v2.0";
    }

    private static TokenValidationParameters BuildTokenValidationParameters(EntraOptions entra)
    {
        var audiences = new List<string>();
        if (!string.IsNullOrWhiteSpace(entra.ApiAudience))
        {
            audiences.Add(entra.ApiAudience!);
        }

        if (!string.IsNullOrWhiteSpace(entra.ApiClientId))
        {
            audiences.Add(entra.ApiClientId!);
        }

        var parameters = new TokenValidationParameters
        {
            NameClaimType = TokenClaims.Name,
            RoleClaimType = TokenClaims.Roles,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(entra.ClockSkewSeconds),
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidAudiences = audiences,
        };

        if (!string.IsNullOrWhiteSpace(entra.IssuerSigningKey))
        {
            parameters.IssuerSigningKey = BuildRsaSecurityKey(entra.IssuerSigningKey!);
        }

        var issuer = BuildIssuer(entra);
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            parameters.ValidIssuer = issuer;
        }

        return parameters;
    }

    private static RsaSecurityKey BuildRsaSecurityKey(string base64SubjectPublicKeyInfo)
    {
        var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64SubjectPublicKeyInfo), out _);
        return new RsaSecurityKey(rsa);
    }
}
