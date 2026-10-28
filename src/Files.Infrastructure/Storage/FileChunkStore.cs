using Files.Application.Storage;
using Microsoft.Extensions.Logging;

namespace Files.Infrastructure.Storage;

/// <summary>
/// On-disk staging store (ADR-0003). Files live under <c>Storage__StagingPath</c> as
/// <c>{sessionId}.part</c>, created with <c>O_EXCL</c> semantics and <c>0600</c> permissions.
/// Chunks are written at a fixed offset and fsynced before the API acknowledges them.
/// </summary>
public sealed partial class FileChunkStore(ILogger<FileChunkStore> logger) : IChunkStore
{
    private const int CopyBufferSize = 64 * 1024;

    public async Task CreateAsync(string stagingPath, CancellationToken ct = default)
    {
        EnsureDirectory(stagingPath);
        await using var stream = new FileStream(
            stagingPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.None);

        try
        {
            SetOwnerOnlyPermissions(stagingPath);
        }
        catch (Exception ex)
        {
            Log.PermissionsWarning(logger, ex);
        }
    }

    public long GetFreeBytes(string directory)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory)) ?? Path.GetFullPath(directory)).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            Log.FreeSpaceWarning(logger, ex);
            return 0;
        }
    }

    public async Task<long> WriteAsync(string stagingPath, long offset, Stream content, long sizeBytes, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            stagingPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.None);
        stream.Seek(offset, SeekOrigin.Begin);

        long written = 0;
        var buffer = new byte[CopyBufferSize];
        while (written < sizeBytes)
        {
            var toRead = (int)Math.Min(CopyBufferSize, sizeBytes - written);
            var read = await content.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            written += read;
        }

        await stream.FlushAsync(ct).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
        return written;
    }

    public Stream OpenRead(string stagingPath) =>
        new FileStream(stagingPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.None);

    public void Delete(string stagingPath)
    {
        try
        {
            File.Delete(stagingPath);
        }
        catch (IOException)
        {
            // Best-effort; the cleanup job also removes orphaned staging files.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public bool Exists(string stagingPath) => File.Exists(stagingPath);

    private static void EnsureDirectory(string stagingPath)
    {
        var directory = Path.GetDirectoryName(stagingPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void SetOwnerOnlyPermissions(string stagingPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(stagingPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set owner-only permissions on the staging file")]
        public static partial void PermissionsWarning(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not determine free disk space")]
        public static partial void FreeSpaceWarning(ILogger logger, Exception exception);
    }
}
