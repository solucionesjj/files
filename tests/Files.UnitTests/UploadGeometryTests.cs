using Files.Application.Storage;
using FluentAssertions;

namespace Files.UnitTests;

public class UploadGeometryTests
{
    private const long ChunkSize = 16L * 1024 * 1024; // 16 MiB

    [Theory]
    [InlineData(0L, 1)]
    [InlineData(1L, 1)]
    [InlineData(16777215L, 1)]
    [InlineData(16777216L, 1)]
    [InlineData(16777217L, 2)]
    [InlineData(5368709120L, 320)] // 5 GiB
    public void Compute_chunk_count_rounds_up_with_a_minimum_of_one(long sizeBytes, int expected)
    {
        UploadGeometry.ComputeChunkCount(sizeBytes, ChunkSize).Should().Be(expected);
    }

    [Fact]
    public void Compute_chunk_count_rejects_a_negative_size()
    {
        var act = () => UploadGeometry.ComputeChunkCount(-1, ChunkSize);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Compute_chunk_count_rejects_a_non_positive_chunk_size()
    {
        var act = () => UploadGeometry.ComputeChunkCount(100, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1, 0L)]
    [InlineData(2, 16777216L)]
    [InlineData(320, 5351931904L)]
    public void Offset_for_chunk_is_chunk_number_minus_one_times_chunk_size(int chunkNumber, long expected)
    {
        UploadGeometry.OffsetForChunk(chunkNumber, ChunkSize).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 2, 20000000, 16777216L)] // non-last chunk is full
    [InlineData(2, 2, 20000000, 3222784L)]  // last chunk holds the remainder
    [InlineData(1, 1, 0, 0L)]               // empty file has a single zero-length chunk
    public void Expected_chunk_size_is_full_except_for_the_last_chunk(
        int chunkNumber, int chunkCount, long sizeBytes, long expected)
    {
        UploadGeometry.ExpectedChunkSize(chunkNumber, chunkCount, sizeBytes, ChunkSize).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(320, true)]
    [InlineData(321, false)]
    public void Chunk_number_range_is_inclusive_one_to_chunk_count(int chunkNumber, bool expected)
    {
        UploadGeometry.IsChunkNumberInRange(chunkNumber, 320).Should().Be(expected);
    }
}
