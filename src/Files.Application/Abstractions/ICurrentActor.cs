using Files.Domain.Enums;

namespace Files.Application.Abstractions;

/// <summary>
/// The authenticated actor for the current request. S3 wires this to the Entra JWT; until then a
/// system/unauthenticated implementation is registered.
/// </summary>
public interface ICurrentActor
{
    ActorType Type { get; }

    Guid? Id { get; }

    bool IsAuthenticated { get; }
}
