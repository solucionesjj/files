namespace Files.Application.Storage;

/// <summary>
/// Pure chunk-geometry math for fixed-size chunked uploads (ADR-0003). A file of size 0 has exactly
/// one zero-length chunk; otherwise the last chunk may be smaller than <c>chunkSizeBytes</c>.
/// </summary>
public static class UploadGeometry
{
    /// <summary>Chunk count for a file size, clamped to at least one chunk.</summary>
    public static int ComputeChunkCount(long sizeBytes, long chunkSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSizeBytes);

        var count = (sizeBytes + chunkSizeBytes - 1) / chunkSizeBytes;
        return count > int.MaxValue ? int.MaxValue : Math.Max(1, (int)count);
    }

    /// <summary>The byte offset at which a 1-based chunk begins.</summary>
    public static long OffsetForChunk(int chunkNumber, long chunkSizeBytes) =>
        (long)(chunkNumber - 1) * chunkSizeBytes;

    /// <summary>
    /// The exact number of bytes a chunk must carry: full size for every chunk except the last,
    /// which holds the remainder (and may be zero for an empty file).
    /// </summary>
    public static long ExpectedChunkSize(int chunkNumber, int chunkCount, long sizeBytes, long chunkSizeBytes)
    {
        if (chunkNumber == chunkCount)
        {
            return sizeBytes - OffsetForChunk(chunkNumber, chunkSizeBytes);
        }

        return chunkSizeBytes;
    }

    /// <summary>Whether a chunk number is within the declared range.</summary>
    public static bool IsChunkNumberInRange(int chunkNumber, int chunkCount) =>
        chunkNumber >= 1 && chunkNumber <= chunkCount;
}
