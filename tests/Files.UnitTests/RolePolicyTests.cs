using Files.Domain.Enums;
using Files.Domain.Identity;
using FluentAssertions;

namespace Files.UnitTests;

public class RolePolicyTests
{
    [Fact]
    public void Satisfies_user_policy_for_plain_user_role()
    {
        RolePolicy.Satisfies(PortalRoles.User, [PortalRoles.User]).Should().BeTrue();
    }

    [Fact]
    public void Satisfies_user_policy_for_admin_because_admin_implies_user()
    {
        RolePolicy.Satisfies(PortalRoles.User, [PortalRoles.Admin]).Should().BeTrue();
    }

    [Fact]
    public void Does_not_satisfy_user_policy_for_auditor_only()
    {
        RolePolicy.Satisfies(PortalRoles.User, [PortalRoles.Auditor]).Should().BeFalse();
    }

    [Fact]
    public void Does_not_satisfy_user_policy_with_no_roles()
    {
        RolePolicy.Satisfies(PortalRoles.User, []).Should().BeFalse();
    }

    [Fact]
    public void Satisfies_admin_policy_only_for_admin_role()
    {
        RolePolicy.Satisfies(PortalRoles.Admin, [PortalRoles.Admin]).Should().BeTrue();
        RolePolicy.Satisfies(PortalRoles.Admin, [PortalRoles.User]).Should().BeFalse();
        RolePolicy.Satisfies(PortalRoles.Admin, [PortalRoles.Auditor]).Should().BeFalse();
    }

    [Fact]
    public void Satisfies_auditor_policy_only_for_auditor_role()
    {
        RolePolicy.Satisfies(PortalRoles.Auditor, [PortalRoles.Auditor]).Should().BeTrue();
        RolePolicy.Satisfies(PortalRoles.Auditor, [PortalRoles.Admin]).Should().BeFalse();
        RolePolicy.Satisfies(PortalRoles.Auditor, [PortalRoles.User]).Should().BeFalse();
    }

    [Fact]
    public void Rejects_unknown_required_role()
    {
        RolePolicy.Satisfies("Files.Superuser", [PortalRoles.Admin]).Should().BeFalse();
    }

    [Fact]
    public void Resolve_actor_type_distinguishes_admin_from_user()
    {
        RolePolicy.ResolveActorType([PortalRoles.Admin]).Should().Be(ActorType.Admin);
        RolePolicy.ResolveActorType([PortalRoles.User]).Should().Be(ActorType.User);
    }
}
