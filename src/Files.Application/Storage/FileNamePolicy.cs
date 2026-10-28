namespace Files.Application.Storage;

/// <summary>Sanitization and validation rules for user-supplied file names (FR-UP-011).</summary>
public static class FileNamePolicy
{
    /// <summary>
    /// Rejects empty/whitespace names, names over <paramref name="maxLength"/> bytes, path
    /// separators, and control characters (which could otherwise break <c>Content-Disposition</c>
    /// or be confused for a path on the host).
    /// </summary>
    public static bool IsValid(string? fileName, int maxLength = 512)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.Length > maxLength)
        {
            return false;
        }

        foreach (var c in fileName)
        {
            if (c == '/' || c == '\\' || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
