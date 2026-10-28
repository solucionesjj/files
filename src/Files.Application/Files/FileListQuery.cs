using System.Text;
using System.Text.Json;
using Files.Domain.Enums;

namespace Files.Application.Files;

/// <summary>Portal file-list sort orders (OpenAPI <c>sort</c> parameter).</summary>
public enum FileSort
{
    CreatedAt,
    CreatedAtDesc,
    Name,
    NameDesc,
    Downloads,
    DownloadsDesc,
}

/// <summary>
/// Decoded keyset cursor for the owner-scoped file list. One of <see cref="CreatedAt"/>,
/// <see cref="Name"/>, or <see cref="Downloads"/> carries the sort key for the requested
/// <see cref="FileSort"/>; <see cref="Id"/> is the deterministic tiebreaker.
/// </summary>
public sealed record FileListCursor(
    DateTimeOffset? CreatedAt,
    string? Name,
    long? Downloads,
    Guid Id);

/// <summary>Owner-scoped search/filter/sort parameters for <c>listMyFiles</c>.</summary>
public sealed record FileListQuery(
    string? Search,
    IReadOnlyList<string> Tags,
    IReadOnlyList<FileStatus>? Statuses,
    FileSort Sort,
    DateTimeOffset? ExpiresBefore,
    DateTimeOffset? ExpiresAfter,
    int? ExpiringWithinDays);

/// <summary>
/// Pure mapping/encoding helpers for the portal file list: query-parameter parsing and the opaque
/// keyset cursor. Kept free of ASP.NET types so it is unit-testable from the Application project.
/// </summary>
public static class FileQueryParsing
{
    public static bool TryParseSort(string? value, out FileSort sort) =>
        (sort = value switch
        {
            null or "" or "-created_at" => FileSort.CreatedAtDesc,
            "created_at" => FileSort.CreatedAt,
            "name" => FileSort.Name,
            "-name" => FileSort.NameDesc,
            "downloads" => FileSort.Downloads,
            "-downloads" => FileSort.DownloadsDesc,
            _ => (FileSort)(-1),
        }) >= 0;

    public static bool TryParseStatus(string value, out FileStatus status) =>
        (status = value switch
        {
            "pending_upload" => FileStatus.PendingUpload,
            "processing" => FileStatus.Processing,
            "available" => FileStatus.Available,
            "quarantined" => FileStatus.Quarantined,
            "expired" => FileStatus.Expired,
            "pending_deletion" => FileStatus.PendingDeletion,
            "deleted" => FileStatus.Deleted,
            "upload_failed" => FileStatus.UploadFailed,
            _ => (FileStatus)(-1),
        }) >= 0;

    public static string EncodeCursor(FileListCursor cursor) =>
        Base64UrlEncode(JsonSerializer.Serialize(cursor));

    public static bool TryDecodeCursor(string? value, out FileListCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            var json = Base64UrlDecode(value);
            var decoded = JsonSerializer.Deserialize<FileListCursor>(json);
            if (decoded is null || decoded.Id == Guid.Empty)
            {
                cursor = null;
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
        padded = padded.PadRight(padded.Length + (4 - (padded.Length % 4)) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
