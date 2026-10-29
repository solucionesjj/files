using System.Text;
using System.Text.Json;
using Files.Domain.Enums;

namespace Files.Application.Files;

/// <summary>Decoded keyset cursor for link lists (newest first on <c>created_at</c> + id tiebreaker).</summary>
public sealed record LinkCursor(DateTimeOffset CreatedAt, Guid Id);

/// <summary>Pure mapping/encoding for link list query parameters and the opaque keyset cursor.</summary>
public static class LinkQueryParsing
{
    public static bool TryParseStatus(string? value, out LinkStatus? status)
    {
        status = value switch
        {
            null or "" => null,
            "active" => LinkStatus.Active,
            "revoked" => LinkStatus.Revoked,
            "expired" => LinkStatus.Expired,
            _ => (LinkStatus)(-1),
        };

        return value is null or "" || status != (LinkStatus)(-1);
    }

    public static string EncodeCursor(LinkCursor cursor) =>
        Base64UrlEncode(JsonSerializer.Serialize(cursor));

    public static bool TryDecodeCursor(string? value, out LinkCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            var json = Base64UrlDecode(value);
            var decoded = JsonSerializer.Deserialize<LinkCursor>(json);
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
        padded = padded.PadRight(padded.Length + (4 - (padded.Length % 4)) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
