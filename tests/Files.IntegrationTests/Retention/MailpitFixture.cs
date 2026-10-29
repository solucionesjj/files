using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Files.IntegrationTests.RetentionApi;

/// <summary>
/// Boots a real Mailpit SMTP sink (axllent/mailpit) on a random host port for the notification
/// integration tests (ADR-0009). SMTP on the mapped 1025 port; the REST API on the mapped 8025 port.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private IContainer? _container;

    public int SmtpPort { get; private set; }

    public string ApiBaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("axllent/mailpit:latest")
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info")))
            .Build();

        await _container.StartAsync();

        SmtpPort = _container.GetMappedPublicPort(1025);
        ApiBaseUrl = $"http://127.0.0.1:{_container.GetMappedPublicPort(8025)}";
    }

    public async Task<IReadOnlyList<JsonElement>> GetMessagesAsync(CancellationToken ct = default)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync($"{ApiBaseUrl}/api/v1/messages", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var messages = body.GetProperty("messages");
        return messages.EnumerateArray().ToArray();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
