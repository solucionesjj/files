namespace Files.Application.Storage;

/// <summary>Thrown when the requested object does not exist in storage (S3 <c>NoSuchKey</c>).</summary>
public sealed class ObjectMissingException : Exception
{
    public ObjectMissingException()
        : base("The requested object does not exist in storage.")
    {
    }
}

/// <summary>A single S3 object's existence and metadata, as returned by <c>HeadObject</c>.</summary>
public sealed record ObjectHead(bool Exists, string? ETag, long SizeBytes, string? ContentType);

/// <summary>One completed multipart part (its number and the ETag S3 returned).</summary>
public sealed record UploadedPart(int PartNumber, string ETag);

/// <summary>
/// Port for the private object store (ADR-0003). The API/worker are the only S3 clients; the
/// implementation reads <c>S3__*</c> and never issues presigned URLs.
/// </summary>
public interface IObjectStore
{
    /// <summary>Uploads an object in a single <c>PutObject</c> (used at or below the single-put threshold).</summary>
    Task<string> PutObjectAsync(
        string key,
        Stream content,
        long sizeBytes,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Starts a multipart upload and returns the S3 upload id.</summary>
    Task<string> InitiateMultipartAsync(string key, string contentType, CancellationToken ct = default);

    /// <summary>Uploads one part of a multipart upload and returns its ETag.</summary>
    Task<UploadedPart> UploadPartAsync(
        string key,
        string uploadId,
        int partNumber,
        Stream content,
        long sizeBytes,
        CancellationToken ct = default);

    /// <summary>Completes a multipart upload and returns the resulting object ETag.</summary>
    Task<string> CompleteMultipartAsync(
        string key,
        string uploadId,
        IReadOnlyList<UploadedPart> parts,
        CancellationToken ct = default);

    /// <summary>Aborts a multipart upload, releasing its parts.</summary>
    Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct = default);

    /// <summary>Checks object existence and metadata without transferring content.</summary>
    Task<ObjectHead> HeadObjectAsync(string key, CancellationToken ct = default);

    /// <summary>Opens the object's content stream (the caller disposes it).</summary>
    Task<Stream> GetObjectAsync(string key, CancellationToken ct = default);
}
