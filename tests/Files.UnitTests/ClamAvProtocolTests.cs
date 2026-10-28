using System.Buffers.Binary;
using System.Text;
using Files.Application.Storage;
using Files.Infrastructure.Storage;
using FluentAssertions;

namespace Files.UnitTests;

public class ClamAvProtocolTests
{
    [Theory]
    [InlineData("stream: OK", ScanResult.Clean)]
    [InlineData("stream: OK\0", ScanResult.Clean)]
    [InlineData("stream: Eicar-Test-Signature FOUND", ScanResult.Infected)]
    [InlineData("stream: Win.Test.EICAR_HDB-1 FOUND\0", ScanResult.Infected)]
    public void Parse_maps_clamd_verdicts(string response, ScanResult expected)
    {
        ClamAvProtocol.Parse(response).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ERROR: something went wrong")]
    [InlineData("1: /path/to/file: OK")]
    [InlineData("stream:")]
    [InlineData("stream: UNKNOWN")]
    public void Parse_maps_unrecognized_responses_to_failed(string response)
    {
        ClamAvProtocol.Parse(response).Should().Be(ScanResult.Failed);
    }

    [Fact]
    public async Task ReadResultAsync_reads_null_terminated_line()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("stream: Eicar-Test-Signature FOUND\0trailing"));

        var result = await ClamAvProtocol.ReadResultAsync(stream, CancellationToken.None);

        result.Should().Be(ScanResult.Infected);
    }

    [Fact]
    public async Task ReadResultAsync_returns_failed_on_eof_without_terminator()
    {
        using var stream = new MemoryStream();

        var result = await ClamAvProtocol.ReadResultAsync(stream, CancellationToken.None);

        result.Should().Be(ScanResult.Failed);
    }

    [Fact]
    public async Task WriteInstreamAsync_frames_chunks_with_big_endian_lengths_and_terminates()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        using var source = new MemoryStream(content);
        using var target = new MemoryStream();

        await ClamAvProtocol.WriteInstreamAsync(target, source, CancellationToken.None);

        var bytes = target.ToArray();
        var command = Encoding.ASCII.GetString(bytes, 0, 9);
        command.Should().Be("INSTREAM\n");

        var offset = 9;
        ReadInt32(bytes, offset).Should().Be(content.Length);
        offset += 4;
        bytes.AsSpan(offset, content.Length).ToArray().Should().Equal(content);
        offset += content.Length;

        // Zero-length chunk terminates the stream.
        ReadInt32(bytes, offset).Should().Be(0);
        offset += 4;
        offset.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task WriteInstreamAsync_empty_content_still_terminates()
    {
        using var source = new MemoryStream();
        using var target = new MemoryStream();

        await ClamAvProtocol.WriteInstreamAsync(target, source, CancellationToken.None);

        var bytes = target.ToArray();
        // "INSTREAM\n" (9 bytes) + a single zero-length chunk header (4 bytes).
        bytes.Length.Should().Be(13);
        ReadInt32(bytes, 9).Should().Be(0);
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
}
