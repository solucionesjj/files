using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Files.Application.Configuration;
using Files.Application.Storage;
using Files.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.UnitTests;

public class ClamAvScannerTests
{
    [Fact]
    public async Task ScanAsync_returns_clean_for_ok_verdict()
    {
        await using var server = new FakeClamd("stream: OK\0");

        var scanner = new ClamAvScanner(
            new ClamAvOptions("127.0.0.1", server.Port, 30),
            NullLogger<ClamAvScanner>.Instance);

        using var content = new MemoryStream([1, 2, 3, 4, 5]);
        var result = await scanner.ScanAsync(content, CancellationToken.None);

        result.Should().Be(ScanResult.Clean);
        server.Payload.Should().Equal(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public async Task ScanAsync_returns_infected_for_found_verdict()
    {
        await using var server = new FakeClamd("stream: Eicar-Test-Signature FOUND\0");

        var scanner = new ClamAvScanner(
            new ClamAvOptions("127.0.0.1", server.Port, 30),
            NullLogger<ClamAvScanner>.Instance);

        using var content = new MemoryStream("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"u8.ToArray());
        var result = await scanner.ScanAsync(content, CancellationToken.None);

        result.Should().Be(ScanResult.Infected);
    }

    [Fact]
    public async Task ScanAsync_returns_failed_on_timeout()
    {
        // The server accepts and reads the request but never replies; the scanner must time out and
        // fail closed rather than hang or upload.
        await using var server = new FakeClamd(respond: false);

        var scanner = new ClamAvScanner(
            new ClamAvOptions("127.0.0.1", server.Port, 1),
            NullLogger<ClamAvScanner>.Instance);

        using var content = new MemoryStream([1, 2, 3]);
        var result = await scanner.ScanAsync(content, CancellationToken.None);

        result.Should().Be(ScanResult.Failed);
    }

    [Fact]
    public async Task ScanAsync_returns_failed_when_connection_is_refused()
    {
        // Reserve a port and close it so the connection is refused.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var scanner = new ClamAvScanner(
            new ClamAvOptions("127.0.0.1", port, 30),
            NullLogger<ClamAvScanner>.Instance);

        using var content = new MemoryStream([1, 2, 3]);
        var result = await scanner.ScanAsync(content, CancellationToken.None);

        result.Should().Be(ScanResult.Failed);
    }

    /// <summary>A minimal clamd stand-in that reads the INSTREAM request and returns a canned verdict.</summary>
    private sealed class FakeClamd : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly byte[] _response;
        private readonly bool _respond;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _accept;

        public FakeClamd(string response) : this(response, respond: true)
        {
        }

        public FakeClamd(bool respond)
            : this("stream: OK\0", respond)
        {
        }

        private FakeClamd(string response, bool respond)
        {
            _response = Encoding.ASCII.GetBytes(response);
            _respond = respond;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accept = Task.Run(AcceptAsync);
        }

        public int Port { get; }

        public byte[] Payload { get; private set; } = [];

        private async Task AcceptAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                await using var stream = client.GetStream();

                // Command line.
                await ReadLineAsync(stream, _cts.Token);

                // Length-prefixed chunks until the zero-length terminator.
                var payload = new MemoryStream();
                while (true)
                {
                    var header = new byte[4];
                    await ReadExactlyAsync(stream, header, _cts.Token);
                    var length = BinaryPrimitives.ReadInt32BigEndian(header);
                    if (length == 0)
                    {
                        break;
                    }

                    var chunk = new byte[length];
                    await ReadExactlyAsync(stream, chunk, _cts.Token);
                    await payload.WriteAsync(chunk, _cts.Token);
                }

                Payload = payload.ToArray();

                if (_respond)
                {
                    await stream.WriteAsync(_response, _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
                else
                {
                    // Hold the connection open so the client times out.
                    await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private static async Task ReadLineAsync(Stream stream, CancellationToken ct)
        {
            var single = new byte[1];
            while (await stream.ReadAsync(single, ct) > 0)
            {
                if (single[0] == (byte)'\n')
                {
                    return;
                }
            }
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read), ct);
                if (count == 0)
                {
                    throw new EndOfStreamException();
                }

                read += count;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                await _accept;
            }
            catch
            {
                // Swallow shutdown races.
            }

            _cts.Dispose();
        }
    }
}
