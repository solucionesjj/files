using System.Security.Claims;
using Files.Api.Authentication;
using Files.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace Files.IntegrationTests.Auth;

/// <summary>
/// Exercises <see cref="IntegrationRequirementHandler"/> against the scope claims emitted by
/// <see cref="ApiKeyAuthenticationHandler"/>: the required permissions must be a subset of the
/// key scopes. No database access is involved.
/// </summary>
public class IntegrationRequirementHandlerTests
{
    [Fact]
    public async Task Grants_access_when_the_required_permission_is_in_the_key_scopes()
    {
        var handler = new IntegrationRequirementHandler();
        var context = CreateContext(
            [new IntegrationRequirement("file.read")],
            Principal("file.read", "file.write"));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Denies_when_the_required_permission_is_missing()
    {
        var handler = new IntegrationRequirementHandler();
        var context = CreateContext(
            [new IntegrationRequirement("file.delete")],
            Principal("file.read"));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_when_there_are_no_scopes()
    {
        var handler = new IntegrationRequirementHandler();
        var context = CreateContext(
            [new IntegrationRequirement("file.read")],
            Principal());

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    private static AuthorizationHandlerContext CreateContext(
        IReadOnlyList<IAuthorizationRequirement> requirements,
        ClaimsPrincipal principal) =>
        new(requirements, principal, resource: null);

    private static ClaimsPrincipal Principal(params string[] scopes)
    {
        var identity = new ClaimsIdentity(ApiKeyAuthenticationHandler.SchemeName);
        foreach (var scope in scopes)
        {
            identity.AddClaim(new Claim(ApiKeyClaims.Scope, scope));
        }

        return new ClaimsPrincipal(identity);
    }
}
