using System.Globalization;
using System.Text;
using System.Text.Json;
using Files.Api.Authentication;
using Files.Application.Abstractions;
using Files.Application.Abstractions.Repositories;
using Files.Application.Identity;
using Files.Domain.Entities;
using Files.Domain.Enums;
using Files.Domain.Events;
using Microsoft.AspNetCore.WebUtilities;

namespace Files.Api.Endpoints;

/// <summary>
/// Admin user management (ADR-0014): roles live in <c>users.role</c>. The first admin is seeded by
/// SQL during deployment; after that admins assign roles here, audited.
/// </summary>
public static class AdminUserEndpoints
{
    private const string UserTarget = "user";

    public static void MapAdminUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/admin/users", ListAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        endpoints.MapPatch("/v1/admin/users/{userId:guid}/role", UpdateRoleAsync)
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IUserRepository users,
        string? cursor,
        int? limit,
        CancellationToken ct)
    {
        var pageLimit = limit ?? 25;
        if (pageLimit is < 1 or > 100)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_query_parameter", "limit must be between 1 and 100");
        }

        DateTimeOffset? cursorCreatedAt;
        try
        {
            cursorCreatedAt = DecodeCursor(cursor);
        }
        catch (FormatException)
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "invalid_cursor", "Invalid cursor");
        }

        var rows = await users.ListAsync(pageLimit + 1, cursorCreatedAt, ct);
        var hasMore = rows.Count > pageLimit;
        var page = rows.Take(pageLimit).ToList();
        var nextCursor = hasMore && page.Count > 0 ? EncodeCursor(page[^1].CreatedAt) : null;

        return Results.Ok(new UsersListResponse(
            page.Select(ToResponse).ToArray(),
            nextCursor,
            hasMore));
    }

    private static async Task<IResult> UpdateRoleAsync(
        Guid userId,
        JsonElement body,
        HttpContext context,
        IIdentityService identityService,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        CancellationToken ct)
    {
        var actor = await identityService.ResolveAsync(ct);
        if (!actor.IsAuthenticated || actor.User is null)
        {
            return Problems.Response(context, StatusCodes.Status401Unauthorized, "invalid_token", "Invalid token");
        }

        if (!body.TryGetProperty("role", out var roleElement)
            || !TryParseRole(roleElement.GetString(), out var newRole))
        {
            return Problems.Response(context, StatusCodes.Status400BadRequest, "validation_error", "role must be one of: user, admin, auditor");
        }

        var reason = ReadNullableString(body, "reason");

        await unitOfWork.BeginTransactionAsync(ct);
        var user = await users.GetByIdTrackedAsync(userId, ct);
        if (user is null)
        {
            await unitOfWork.RollbackAsync(ct);
            return Problems.Response(context, StatusCodes.Status404NotFound, "user_not_found", "User not found");
        }

        if (user.Role == newRole)
        {
            await unitOfWork.RollbackAsync(ct);
            return Results.Ok(ToResponse(user));
        }

        if (user.Role == UserRole.Admin && newRole != UserRole.Admin
            && await users.CountByRoleAsync(UserRole.Admin, ct) <= 1)
        {
            await unitOfWork.RollbackAsync(ct);
            return Problems.Response(context, StatusCodes.Status409Conflict, "last_admin", "Cannot demote the last administrator");
        }

        var before = ToResponse(user);
        user.Role = newRole;
        var after = ToResponse(user);

        await auditWriter.WriteAdminAuditAsync(new AdminAuditEvent
        {
            ActorUserId = actor.Id,
            ActorRole = string.Join(',', actor.Roles),
            Action = "user.role.updated",
            TargetType = UserTarget,
            TargetId = user.Id,
            Reason = reason,
            Metadata = JsonSerializer.Serialize(new { before, after }),
            TraceId = RequestContext.TraceId(context),
            IpHash = RequestContext.IpHash(context),
        }, ct);

        await outboxWriter.WriteAsync(new OutboxMessage
        {
            EventType = "user.role.updated",
            AggregateType = UserTarget,
            AggregateId = user.Id,
            Payload = JsonSerializer.Serialize(new { userId = user.Id, role = RoleName(newRole) }),
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.CommitAsync(ct);

        return Results.Ok(ToResponse(user));
    }

    private static bool TryParseRole(string? value, out UserRole role)
    {
        role = value switch
        {
            "user" => UserRole.User,
            "admin" => UserRole.Admin,
            "auditor" => UserRole.Auditor,
            _ => default,
        };
        return value is "user" or "admin" or "auditor";
    }

    private static string RoleName(UserRole role) => role.ToString().ToLowerInvariant();

    private static string StatusName(UserStatus status) => status == UserStatus.Active ? "active" : "disabled";

    private static UserResponse ToResponse(User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        RoleName(user.Role),
        StatusName(user.Status),
        user.CreatedAt,
        user.LastLoginAt);

    private static string? ReadNullableString(JsonElement body, string property)
    {
        if (!body.TryGetProperty(property, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.Null ? null : element.GetString();
    }

    private static string EncodeCursor(DateTimeOffset createdAt) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(createdAt.ToString("O", CultureInfo.InvariantCulture)));

    private static DateTimeOffset? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public sealed record UsersListResponse(
        IReadOnlyList<UserResponse> Items,
        string? NextCursor,
        bool HasMore);
}
