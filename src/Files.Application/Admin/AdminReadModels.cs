using System.Text;
using System.Text.Json;

namespace Files.Application.Admin;

/// <summary>
/// Read models for the read-only admin/audit surface (FR-ADM-005/006/008, FR-AUD-001..003). These
/// are projection records returned by the Dapper repositories; the tables themselves remain the
/// canonical schema owned by Flyway.
/// </summary>

public sealed record AuditEventRecord(
    Guid Id,
    string Category,
    Guid? FileId,
    string ActorType,
    Guid? ActorId,
    string Action,
    string Result,
    string? Reason,
    string? Metadata,
    string? TraceId,
    DateTimeOffset OccurredAt);

/// <summary>Keyset cursor for audit/download-event lists ordered by <c>(occurred_at DESC, id DESC)</c>.</summary>
public sealed record AuditEventCursor(DateTimeOffset OccurredAt, Guid Id);

public sealed record AuditEventQuery(
    string? Category,
    Guid? FileId,
    string? ActorType,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    AuditEventCursor? Cursor);

public sealed record DownloadEventQuery(
    Guid? FileId,
    Guid? LinkId,
    string? Result,
    DateTimeOffset? From,
    DateTimeOffset? To,
    AuditEventCursor? Cursor);

public sealed record IntegrationUsage(Guid IntegrationId, string Name, long Files, long Bytes);

public sealed record UsageStatsResult(
    long StorageTotalBytes,
    long StorageHumanBytes,
    long StorageIntegrationBytes,
    long FilesTotal,
    long FilesAvailable,
    long FilesExpired,
    long FilesPendingDeletion,
    long DownloadsLast30Days,
    long DownloadsLast7Days,
    IReadOnlyList<IntegrationUsage> Integrations);

/// <summary>
/// Pure parsing/encoding helpers for the admin audit read surface, kept free of ASP.NET types so
/// they are unit-testable from the Application project.
/// </summary>
public static class AdminAuditQueryParsing
{
    public static string EncodeCursor(AuditEventCursor cursor) =>
        Base64UrlEncode(JsonSerializer.Serialize(cursor));

    public static bool TryDecodeCursor(string? value, out AuditEventCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            var json = Base64UrlDecode(value);
            var decoded = JsonSerializer.Deserialize<AuditEventCursor>(json);
            if (decoded is null || decoded.Id == Guid.Empty)
            {
                return false;
            }

            cursor = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
