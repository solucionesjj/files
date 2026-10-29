namespace Files.Application.Storage;

/// <summary>
/// Builds an RFC 6266/5987 <c>Content-Disposition</c> header for a download: an ASCII-only
/// <c>filename</c> fallback (control characters, quotes, and backslashes replaced so the header can
/// never be broken or inject additional headers) plus a <c>filename*=UTF-8''…</c> extended value that
/// percent-encodes the original name (T-05, FR-UP-011, ADR-0003 §8).
/// </summary>
public static class ContentDisposition
{
    public static string Build(string fileName)
    {
        var ascii = new string(fileName
            .Select(c => c >= 32 && c < 127 && c != '"' && c != '\\' ? c : '_')
            .ToArray());
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}
