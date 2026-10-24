using Files.Application.Abstractions;
using Files.Application.Identity;
using Files.Domain.Enums;

namespace Files.Api.Identity;

/// <summary>
/// Token-backed <see cref="ICurrentActor"/>. The actor is resolved once per request (by
/// <see cref="CurrentActorMiddleware"/>); until then it reports the unauthenticated system actor.
/// </summary>
public sealed class TokenCurrentActor(IIdentityService identityService) : ICurrentActor
{
    private ResolvedActor? _resolved;

    public ActorType Type => _resolved?.Type ?? ActorType.System;

    public Guid? Id => _resolved?.Id;

    public bool IsAuthenticated => _resolved?.IsAuthenticated ?? false;

    public async ValueTask InitializeAsync(CancellationToken ct = default) =>
        _resolved = await identityService.ResolveAsync(ct);
}
