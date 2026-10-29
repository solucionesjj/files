using Files.Domain.Entities;
using Files.Domain.Enums;

namespace Files.Application.Files;

/// <summary>
/// Domain rule #4: a file is downloadable iff <c>status = available</c>, its malware status is
/// <c>clean</c> (or <c>not_scanned</c> when scanning is disabled), and it is not logically expired.
/// A legal hold blocks deletion/expiry, never a normally available, unexpired file.
/// </summary>
public static class FileDownloadability
{
    public static bool IsDownloadable(FileEntity file, DateTimeOffset now) =>
        file.Status == FileStatus.Available
        && (file.ExpiresAt is null || file.ExpiresAt > now)
        && file.ScanStatus is ScanStatus.Clean or ScanStatus.NotScanned;
}
