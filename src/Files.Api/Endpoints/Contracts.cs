using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Api.Endpoints;

public sealed record CreateIntegrationRequest(
    string Slug,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string>? AllowedIpCidrs,
    Guid? EntraClientId);

public sealed record SetPermissionsRequest(IReadOnlyList<string> Permissions);

public sealed record IntegrationResponse(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    string Status,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> AllowedIpCidrs,
    Guid? EntraClientId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DisabledAt,
    string? DisabledReason);

public sealed record IntegrationListResponse(
    IReadOnlyList<IntegrationResponse> Items,
    string? NextCursor,
    bool HasMore);

public sealed record MeResponse(
    Guid UserId,
    Guid TenantId,
    string? Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public static class IntegrationMapper
{
    public static IntegrationResponse ToResponse(Integration integration) => new(
        integration.Id,
        integration.Slug,
        integration.Name,
        integration.Description,
        integration.Status == IntegrationStatus.Active ? "active" : "disabled",
        integration.Permissions.Select(p => p.Permission).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
        integration.AllowedIpCidrs.Select(cidr => cidr.ToString()).ToArray(),
        integration.EntraClientId,
        integration.CreatedAt,
        integration.UpdatedAt,
        integration.DisabledAt,
        integration.DisabledReason);
}
