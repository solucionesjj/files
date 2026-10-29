using System.Text;
using System.Text.Json;
using Files.Application.Abstractions.Repositories;

namespace Files.Application.Integrations;

/// <summary>
/// Opaque base64url cursor codec for the integration file list (keyset on
/// <c>created_at DESC, id DESC</c>). Kept free of ASP.NET types so it is unit-testable.
/// </summary>
public static class IntegrationFileCursorParsing
{
    public static string Encode(IntegrationFileCursor cursor) =>
        Base64UrlEncode(JsonSerializer.Serialize(cursor));

    public static bool TryDecode(string? value, out IntegrationFileCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<IntegrationFileCursor>(Base64UrlDecode(value));
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
