using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Files.Domain.Entities;

namespace Files.Application.Notifications;

/// <summary>Template variables (strict allowlist, ADR-0009). Never credentials, keys, or file contents.</summary>
public sealed record EmailVariables(
    string FileName,
    DateTimeOffset ExpiresAt,
    int IntervalDays,
    string OwnerName,
    string PortalUrl,
    string FileId);

/// <summary>A rendered subject/html/text triple ready to send.</summary>
public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Renders an <c>email_templates</c> template against the strict variable allowlist. Unknown
/// placeholders fail rendering (no send) rather than leaking data; HTML bodies are escaped.
/// </summary>
public static partial class EmailTemplateRenderer
{
    private static readonly HashSet<string> AllowedVariables = new(StringComparer.Ordinal)
    {
        "fileName", "expiresAt", "ownerName", "portalUrl", "intervalDays", "fileId",
    };

    public static RenderedEmail? Render(EmailTemplate template, EmailVariables variables)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        var culture = ResolveCulture(template.Locale);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fileName"] = variables.FileName,
            ["expiresAt"] = variables.ExpiresAt.ToString("d", culture),
            ["ownerName"] = variables.OwnerName,
            ["portalUrl"] = variables.PortalUrl,
            ["intervalDays"] = variables.IntervalDays.ToString(culture),
            ["fileId"] = variables.FileId,
        };

        if (!TryRender(template.Subject, values, html: false, out var subject)
            || !TryRender(template.BodyText, values, html: false, out var textBody)
            || !TryRender(template.BodyHtml, values, html: true, out var htmlBody))
        {
            return null;
        }

        return new RenderedEmail(subject, htmlBody, textBody);
    }

    private static bool TryRender(
        string template,
        Dictionary<string, string> values,
        bool html,
        out string rendered)
    {
        rendered = string.Empty;
        var matches = Placeholder().Matches(template);

        var builder = new System.Text.StringBuilder(template.Length);
        var position = 0;
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            if (!AllowedVariables.Contains(name))
            {
                return false;
            }

            builder.Append(template, position, match.Index - position);
            var value = values[name];
            builder.Append(html ? WebUtility.HtmlEncode(value) : value);
            position = match.Index + match.Length;
        }

        builder.Append(template, position, template.Length - position);
        rendered = builder.ToString();
        return true;
    }

    private static CultureInfo ResolveCulture(string locale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    [GeneratedRegex(@"\{\{([a-zA-Z0-9]+)\}\}")]
    private static partial Regex Placeholder();
}
