using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Identity;

/// <summary>
/// The authenticated actor resolved for the current request. <see cref="User"/> is populated (and
/// provisioned JIT) for human tokens; <see cref="Integration"/> is populated (active integration +
/// DB permissions) for machine tokens.
/// </summary>
public sealed record ResolvedActor(
    ActorType Type,
    Guid? Id,
    bool IsAuthenticated,
    User? User,
    Integration? Integration,
    IReadOnlyList<string> Roles,
    IReadOnlySet<string> Permissions)
{
    public static ResolvedActor System { get; } =
        new(ActorType.System, null, false, null, null, [], new HashSet<string>(StringComparer.Ordinal));
}
