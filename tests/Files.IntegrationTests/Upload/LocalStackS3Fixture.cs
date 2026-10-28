using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Files.IntegrationTests.Upload;

/// <summary>
/// Boots a real LocalStack S3 container and creates the private <c>files-local</c> bucket the
/// upload tests commit into (NFR-015). The API host is pointed at this container's endpoint.
/// </summary>
public sealed class LocalStackS3Fixture : IAsyncLifetime
{
    public const string Bucket = "files-local";
    public const string Region = "us-east-1";

    private IContainer? _container;

    public string ServiceUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("localstack/localstack:3.8")
            .WithEnvironment("SERVICES", "s3")
            .WithEnvironment("DEBUG", "0")
            .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
            .WithEnvironment("AWS_ACCESS_KEY_ID", "test")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
            .WithPortBinding(4566, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(4566).ForPath("/_localstack/health")))
            .Build();

        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(4566);
        ServiceUrl = $"http://localhost:{port}";

        await CreateBucketWithRetryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private async Task CreateBucketWithRetryAsync()
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = true,
            UseHttp = true,
            AuthenticationRegion = Region,
            ServiceURL = ServiceUrl,
        };

        using var client = new AmazonS3Client(new BasicAWSCredentials("test", "test"), config);

        // S3 inside LocalStack may take a moment after the health endpoint reports ready.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await client.PutBucketAsync(Bucket);
                return;
            }
            catch (AmazonS3Exception) when (attempt < 19)
            {
                await Task.Delay(250);
            }
        }
    }
}
