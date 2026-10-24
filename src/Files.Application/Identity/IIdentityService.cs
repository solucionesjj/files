namespace Files.Application.Identity;

/// <summary>
/// Resolves the current authenticated actor from the request's validated token. Resolution is
/// idempotent within a request (results are cached on the scoped implementation).
/// </summary>
public interface IIdentityService
{
    ValueTask<ResolvedActor> ResolveAsync(CancellationToken ct = default);
}
