using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// Boots a real ClamAV daemon (clamd) container for the scan-before-commit integration tests
/// (D-35). The <c>clamav/clamav:1.4</c> image is amd64-only, so on Apple Silicon it may require
/// emulation; when it cannot start (or when <c>FILES_CLAMAV_TESTS=0</c>) the fixture reports
/// <see cref="Available"/> as <c>false</c> and the ClamAV tests skip. CI (Linux) runs them for real.
/// </summary>
public sealed class ClamAvFixture : IAsyncLifetime
{
    private IContainer? _container;

    public bool Available { get; private set; }

    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        var optOut = Environment.GetEnvironmentVariable("FILES_CLAMAV_TESTS");
        if (string.Equals(optOut, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(optOut, "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var clamdConf = Path.Combine(AppContext.BaseDirectory, "clamav", "clamd.conf");
            _container = new ContainerBuilder("clamav/clamav:1.4")
                .WithEnvironment("CLAMAV_NO_MILTERD", "true")
                .WithEnvironment("CLAMAV_NO_CLAMD", "false")
                .WithEnvironment("CLAMAV_NO_FRESHCLAM", "false")
                .WithResourceMapping(new FileInfo(clamdConf), "/etc/clamav/clamd.conf")
                .WithPortBinding(3310, true)
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(3310);
            Available = await WaitUntilReadyAsync();
        }
        catch (Exception)
        {
            // Emulation/registry failures on non-Linux hosts: keep the collection alive and skip.
            Available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private async Task<bool> WaitUntilReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await tcp.ConnectAsync("127.0.0.1", Port, timeout.Token);
                return true;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        return false;
    }
}
