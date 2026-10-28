namespace Files.Application.Storage;

/// <summary>
/// Port for the local staging store (ADR-0003). Chunks are written to a private file on the host
/// disk at a fixed offset and fsynced before the API acknowledges them; the assembled file is then
/// read back for verification and the server-side S3 commit.
/// </summary>
public interface IChunkStore
{
    /// <summary>
    /// Creates a new, empty staging file with <c>O_EXCL</c> semantics and owner-only permissions
    /// (<c>0600</c>). Throws <see cref="IOException"/> when the file already exists.
    /// </summary>
    Task CreateAsync(string stagingPath, CancellationToken ct = default);

    /// <summary>Returns the free bytes on the volume backing <paramref name="directory"/>.</summary>
    long GetFreeBytes(string directory);

    /// <summary>
    /// Writes exactly <paramref name="sizeBytes"/> from <paramref name="content"/> to the staging
    /// file at <paramref name="offset"/>, then fsyncs. Returns the number of bytes actually written.
    /// </summary>
    Task<long> WriteAsync(string stagingPath, long offset, Stream content, long sizeBytes, CancellationToken ct = default);

    /// <summary>Opens the staging file for reading (the caller disposes it).</summary>
    Stream OpenRead(string stagingPath);

    /// <summary>Deletes the staging file if it exists.</summary>
    void Delete(string stagingPath);

    /// <summary>Whether the staging file currently exists.</summary>
    bool Exists(string stagingPath);
}
