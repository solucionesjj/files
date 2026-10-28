using System.Text.RegularExpressions;

namespace Files.Application.Storage;

/// <summary>Validation for declared content types (FR-UP-012).</summary>
public static partial class ContentTypePolicy
{
    private static readonly Regex MediaType = MediaTypeRegex();

    /// <summary>
    /// Accepts a well-formed media type (<c>type/subtype</c>) of reasonable length. A full
    /// allowlist is a deployment-level setting and is not enforced here (SHOULD in FR-UP-012).
    /// </summary>
    public static bool IsValid(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType)
        && contentType.Length is >= 3 and <= 255
        && MediaType.IsMatch(contentType);

    [GeneratedRegex(@"^[a-zA-Z0-9!#$&^_.+-]+/[a-zA-Z0-9!#$&^_.+-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaTypeRegex();
}
