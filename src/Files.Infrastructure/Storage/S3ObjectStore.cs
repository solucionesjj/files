using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Files.Application.Configuration;
using Files.Application.Storage;

namespace Files.Infrastructure.Storage;

/// <summary>
/// AWS S3 implementation of <see cref="IObjectStore"/> (ADR-0003). Locally
/// <see cref="S3Options.ServiceUrl"/> points at LocalStack with path-style HTTP; production uses the
/// AWS endpoint with a static access key read from the environment. No presigned URL is ever issued.
/// </summary>
public sealed class S3ObjectStore : IObjectStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3ObjectStore(S3Options options)
    {
        _bucket = options.Bucket;

        // RegionEndpoint and ServiceURL are mutually exclusive in the SDK: when a custom endpoint
        // (LocalStack in dev/test) is configured, set the signing region via AuthenticationRegion
        // and the endpoint via ServiceURL; otherwise resolve the real AWS region endpoint.
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle,
            UseHttp = !options.UseSsl,
            // LocalStack does not consistently honor the v4 automatic CRC checksums; only request
            // or validate checksums when the operation actually requires them.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.AuthenticationRegion = options.Region;
            config.ServiceURL = options.ServiceUrl;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        var credentials = new BasicAWSCredentials(options.AccessKeyId ?? string.Empty, options.SecretAccessKey ?? string.Empty);
        _client = new AmazonS3Client(credentials, config);
    }

    public async Task<string> PutObjectAsync(string key, Stream content, long sizeBytes, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        };

        var response = await _client.PutObjectAsync(request, ct).ConfigureAwait(false);
        return response.ETag ?? string.Empty;
    }

    public async Task<string> InitiateMultipartAsync(string key, string contentType, CancellationToken ct = default)
    {
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            ContentType = contentType,
        };

        var response = await _client.InitiateMultipartUploadAsync(request, ct).ConfigureAwait(false);
        return response.UploadId;
    }

    public async Task<UploadedPart> UploadPartAsync(string key, string uploadId, int partNumber, Stream content, long sizeBytes, CancellationToken ct = default)
    {
        var request = new UploadPartRequest
        {
            BucketName = _bucket,
            Key = key,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = content,
        };

        var response = await _client.UploadPartAsync(request, ct).ConfigureAwait(false);
        return new UploadedPart(partNumber, response.ETag);
    }

    public async Task<string> CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken ct = default)
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            UploadId = uploadId,
            PartETags = parts
                .OrderBy(p => p.PartNumber)
                .Select(p => new PartETag(p.PartNumber, p.ETag))
                .ToList(),
        };

        var response = await _client.CompleteMultipartUploadAsync(request, ct).ConfigureAwait(false);
        return response.ETag ?? string.Empty;
    }

    public Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct = default)
    {
        var request = new AbortMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            UploadId = uploadId,
        };

        return _client.AbortMultipartUploadAsync(request, ct);
    }

    public async Task<ObjectHead> HeadObjectAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest { BucketName = _bucket, Key = key };
            var response = await _client.GetObjectMetadataAsync(request, ct).ConfigureAwait(false);
            return new ObjectHead(true, response.ETag, response.ContentLength, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new ObjectHead(false, null, 0, null);
        }
    }

    public async Task<Stream> GetObjectAsync(string key, CancellationToken ct = default)
    {
        var request = new GetObjectRequest { BucketName = _bucket, Key = key };
        try
        {
            var response = await _client.GetObjectAsync(request, ct).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ObjectMissingException();
        }
    }

    public async Task<Stream> GetObjectAsync(string key, long offset, long length, CancellationToken ct = default)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            ByteRange = new ByteRange(offset, offset + length - 1),
        };

        try
        {
            var response = await _client.GetObjectAsync(request, ct).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ObjectMissingException();
        }
    }

    public void Dispose() => _client.Dispose();
}
