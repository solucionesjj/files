using System.Security.Claims;
using Files.Application.Identity;
using FluentAssertions;

namespace Files.UnitTests;

public class TokenClaimsTests
{
    [Fact]
    public void Parses_tenant_and_object_ids()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var principal = Principal(("tid", tenantId.ToString()), ("oid", objectId.ToString()));

        TokenClaims.GetTenantId(principal).Should().Be(tenantId);
        TokenClaims.GetObjectId(principal).Should().Be(objectId);
    }

    [Fact]
    public void Returns_null_for_missing_or_malformed_guids()
    {
        TokenClaims.GetTenantId(Principal()).Should().BeNull();
        TokenClaims.GetObjectId(Principal(("oid", "not-a-guid"))).Should().BeNull();
    }

    [Fact]
    public void Client_id_prefers_azp_over_appid()
    {
        var azp = Guid.NewGuid();
        var appid = Guid.NewGuid();
        var principal = Principal(("azp", azp.ToString()), ("appid", appid.ToString()));

        TokenClaims.GetClientId(principal).Should().Be(azp);
        TokenClaims.GetClientId(Principal(("appid", appid.ToString()))).Should().Be(appid);
        TokenClaims.GetClientId(Principal()).Should().BeNull();
    }

    [Fact]
    public void Is_app_only_true_when_idtyp_is_app()
    {
        TokenClaims.IsAppOnly(Principal(("idtyp", "app"))).Should().BeTrue();
        TokenClaims.IsAppOnly(Principal(("idtyp", "user"))).Should().BeFalse();
        TokenClaims.IsAppOnly(Principal()).Should().BeFalse();
    }

    [Fact]
    public void Classifies_app_only_tokens_as_integrations()
    {
        TokenClaims.Classify(Principal(("idtyp", "app"))).Should().Be(TokenKind.Integration);
        TokenClaims.Classify(Principal(("idtyp", "user"))).Should().Be(TokenKind.User);
        TokenClaims.Classify(Principal()).Should().Be(TokenKind.User);
    }

    [Fact]
    public void Email_prefers_preferred_username_with_at_sign()
    {
        TokenClaims.GetEmail(Principal(("preferred_username", "jane@example.com"), ("email", "other@example.com")))
            .Should().Be("jane@example.com");

        // preferred_username without @ falls back to email
        TokenClaims.GetEmail(Principal(("preferred_username", "jane"), ("email", "other@example.com")))
            .Should().Be("other@example.com");

        TokenClaims.GetEmail(Principal(("email", "only@example.com"))).Should().Be("only@example.com");
        TokenClaims.GetEmail(Principal()).Should().BeNull();
    }

    [Fact]
    public void Display_name_falls_back_to_email_then_unknown()
    {
        TokenClaims.GetDisplayName(Principal(("name", "Jane Doe"))).Should().Be("Jane Doe");
        TokenClaims.GetDisplayName(Principal(("email", "jane@example.com"))).Should().Be("jane@example.com");
        TokenClaims.GetDisplayName(Principal()).Should().Be("Unknown");
    }

    [Fact]
    public void Roles_are_collected_from_the_roles_claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("roles", "Files.User"),
            new Claim("roles", "Files.Admin"),
        ], "test"));

        TokenClaims.GetRoles(principal).Should().BeEquivalentTo(["Files.User", "Files.Admin"]);
    }

    [Fact]
    public void Scope_matching_is_space_delimited()
    {
        TokenClaims.HasScope(Principal(("scp", "Files.Access openid")), "Files.Access").Should().BeTrue();
        TokenClaims.HasScope(Principal(("scp", "Files.Access")), "Files.Write").Should().BeFalse();
        TokenClaims.HasScope(Principal(), "Files.Access").Should().BeFalse();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));
}
