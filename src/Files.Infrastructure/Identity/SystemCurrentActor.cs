using Files.Application.Abstractions;
using Files.Domain.Enums;

namespace Files.Infrastructure.Identity;

/// <summary>
/// Default <see cref="ICurrentActor"/> for S2, before Entra auth is wired (S3). Represents an
/// unauthenticated/system actor so audit and outbox rows always carry a valid actor type.
/// </summary>
public sealed class SystemCurrentActor : ICurrentActor
{
    public ActorType Type => ActorType.System;

    public Guid? Id => null;

    public bool IsAuthenticated => false;
}
