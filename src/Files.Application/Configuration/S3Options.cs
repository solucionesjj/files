using Microsoft.Extensions.Configuration;

namespace Files.Application.Configuration;

/// <summary>
/// S3 client configuration (ADR-0003). Locally <see cref="ServiceUrl"/> points at LocalStack and
/// path-style/HTTP are forced; production uses the AWS endpoint with a static access key read from
/// the environment (no instance role).
/// </summary>
public sealed record S3Options(
    string Region,
    string Bucket,
    string? ServiceUrl,
    bool ForcePathStyle,
    bool UseSsl,
    string? AccessKeyId,
    string? SecretAccessKey)
{
    public static S3Options FromConfiguration(IConfiguration configuration) => new(
        Region: configuration["S3:Region"] ?? "us-east-1",
        Bucket: configuration["S3:Bucket"] ?? "files-local",
        ServiceUrl: NullIfEmpty(configuration["S3:ServiceUrl"]),
        ForcePathStyle: configuration.GetValue("S3:ForcePathStyle", false),
        UseSsl: configuration.GetValue("S3:UseSsl", true),
        AccessKeyId: NullIfEmpty(configuration["S3:AccessKeyId"]),
        SecretAccessKey: NullIfEmpty(configuration["S3:SecretAccessKey"]));

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
