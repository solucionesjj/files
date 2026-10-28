using System.Net.Sockets;
using Files.Application.Configuration;
using Files.Application.Storage;
using Microsoft.Extensions.Logging;

namespace Files.Infrastructure.Storage;

/// <summary>
/// ClamAV implementation of <see cref="IMalwareScanner"/> (D-35). Streams the staged file to clamd
/// over TCP using the <c>INSTREAM</c> protocol and never buffers more than a fixed chunk, so a
/// 5 GiB file does not occupy memory. The file name is never logged; only the scan outcome is.
/// </summary>
public sealed partial class ClamAvScanner(
    ClamAvOptions options,
    ILogger<ClamAvScanner> logger) : IMalwareScanner
{
    private static readonly int BufferSize = 64 * 1024;

    public async Task<ScanResult> ScanAsync(string filePath, CancellationToken ct = default)
    {
        await using var content = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        return await ScanAsync(content, ct).ConfigureAwait(false);
    }

    internal async Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ScanTimeoutSeconds));

        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(options.Host, options.Port, timeout.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();

            await ClamAvProtocol.WriteInstreamAsync(stream, content, timeout.Token).ConfigureAwait(false);
            return await ClamAvProtocol.ReadResultAsync(stream, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.ScanTimedOut(logger);
            return ScanResult.Failed;
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            Log.ScanFailed(logger, ex);
            return ScanResult.Failed;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "ClamAV scan timed out; the file will be retried")]
        public static partial void ScanTimedOut(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "ClamAV scan failed; the file will be retried")]
        public static partial void ScanFailed(ILogger logger, Exception exception);
    }
}
