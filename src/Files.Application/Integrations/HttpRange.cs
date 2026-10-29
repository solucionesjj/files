using System.Globalization;

namespace Files.Application.Integrations;

/// <summary>Outcome of parsing the <c>Range</c> header for a single byte range.</summary>
public enum ByteRangeParse
{
    /// <summary>No <c>Range</c> header (or empty) — return the whole entity.</summary>
    NotRequested,

    /// <summary>A single satisfiable byte range was parsed into <c>offset</c>/<c>length</c>.</summary>
    Satisfiable,

    /// <summary>A syntactically valid single range that cannot be satisfied → 416.</summary>
    Unsatisfiable,

    /// <summary>Malformed or multi-range — ignore and return the whole entity (RFC 9110 §14.2).</summary>
    Ignore,
}

/// <summary>
/// Pure single-range parser for the integration content endpoint. Only <c>bytes=a-b</c>,
/// <c>bytes=a-</c>, and <c>bytes=-n</c> are accepted; multi-range requests are ignored.
/// </summary>
public static class HttpRange
{
    public static ByteRangeParse Parse(string? header, long totalLength, out long offset, out long length)
    {
        offset = 0;
        length = totalLength;

        if (string.IsNullOrWhiteSpace(header))
        {
            return ByteRangeParse.NotRequested;
        }

        var trimmed = header.Trim();
        if (!trimmed.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return ByteRangeParse.Ignore;
        }

        var spec = trimmed["bytes=".Length..].Trim();
        if (spec.Length == 0 || spec.Contains(','))
        {
            return ByteRangeParse.Ignore;
        }

        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return ByteRangeParse.Ignore;
        }

        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();

        if (first.Length == 0)
        {
            // Suffix range: `bytes=-N` — the last N bytes.
            if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0)
            {
                return ByteRangeParse.Ignore;
            }

            if (totalLength == 0)
            {
                return ByteRangeParse.Unsatisfiable;
            }

            var suffixLength = Math.Min(suffix, totalLength);
            offset = totalLength - suffixLength;
            length = suffixLength;
            return ByteRangeParse.Satisfiable;
        }

        if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var start) || start < 0)
        {
            return ByteRangeParse.Ignore;
        }

        if (last.Length == 0)
        {
            // `bytes=a-` — from start to the end.
            if (start >= totalLength)
            {
                return ByteRangeParse.Unsatisfiable;
            }

            offset = start;
            length = totalLength - start;
            return ByteRangeParse.Satisfiable;
        }

        if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var end) || end < start)
        {
            return ByteRangeParse.Ignore;
        }

        if (start >= totalLength)
        {
            return ByteRangeParse.Unsatisfiable;
        }

        offset = start;
        length = Math.Min(end - start + 1, totalLength - start);
        return ByteRangeParse.Satisfiable;
    }
}
