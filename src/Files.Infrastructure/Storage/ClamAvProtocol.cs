using System.Buffers.Binary;
using System.Text;
using Files.Application.Storage;

namespace Files.Infrastructure.Storage;

/// <summary>
/// The clamd <c>INSTREAM</c> wire protocol (chunked streaming). The client sends the
/// <c>INSTREAM</c> command, then the file as a sequence of length-prefixed chunks (4-byte
/// network-order length + payload), terminated by a zero-length chunk. clamd replies with a single
/// null-terminated line: <c>stream: OK</c> (clean) or <c>stream: &lt;name&gt; FOUND</c> (infected).
/// </summary>
internal static class ClamAvProtocol
{
    private static readonly byte[] Command = Encoding.ASCII.GetBytes("INSTREAM\n");

    public static async Task WriteInstreamAsync(Stream target, Stream content, CancellationToken ct)
    {
        await target.WriteAsync(Command, ct).ConfigureAwait(false);

        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await content.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await WriteChunkAsync(target, buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        // A zero-length chunk terminates the stream and tells clamd the file is complete.
        await WriteChunkAsync(target, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
        await target.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<ScanResult> ReadResultAsync(Stream source, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var single = new byte[1];
        while (true)
        {
            var read = await source.ReadAsync(single, ct).ConfigureAwait(false);
            if (read == 0)
            {
                // EOF before a terminator: treat as a protocol failure (fail closed).
                return ScanResult.Failed;
            }

            var value = single[0];
            if (value is (byte)'\0' or (byte)'\n')
            {
                break;
            }

            buffer.WriteByte(value);
        }

        return Parse(Encoding.ASCII.GetString(buffer.ToArray()));
    }

    internal static ScanResult Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return ScanResult.Failed;
        }

        // The wire terminator (a null byte) may still be present when parsing a raw reply.
        var trimmed = response.TrimEnd('\0', '\n', '\r').Trim();
        if (!trimmed.StartsWith("stream:", StringComparison.OrdinalIgnoreCase))
        {
            return ScanResult.Failed;
        }

        var verdict = trimmed["stream:".Length..].Trim();
        if (verdict.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            return ScanResult.Clean;
        }

        // A verdict such as "Eicar-Test-Signature FOUND" means the file is infected.
        if (verdict.EndsWith(" FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return ScanResult.Infected;
        }

        return ScanResult.Failed;
    }

    private static async Task WriteChunkAsync(Stream target, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await target.WriteAsync(header, ct).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await target.WriteAsync(payload, ct).ConfigureAwait(false);
        }
    }
}
