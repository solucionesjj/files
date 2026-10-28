using Files.Application.Files;
using Files.Domain.Enums;
using FluentAssertions;

namespace Files.UnitTests;

public class FileListQueryParsingTests
{
    [Theory]
    [InlineData(null, FileSort.CreatedAtDesc)]
    [InlineData("", FileSort.CreatedAtDesc)]
    [InlineData("created_at", FileSort.CreatedAt)]
    [InlineData("-created_at", FileSort.CreatedAtDesc)]
    [InlineData("name", FileSort.Name)]
    [InlineData("-name", FileSort.NameDesc)]
    [InlineData("downloads", FileSort.Downloads)]
    [InlineData("-downloads", FileSort.DownloadsDesc)]
    public void TryParseSort_maps_known_and_default_values(string? input, FileSort expected)
    {
        FileQueryParsing.TryParseSort(input, out var sort).Should().BeTrue();
        sort.Should().Be(expected);
    }

    [Fact]
    public void TryParseSort_rejects_unknown_values()
    {
        FileQueryParsing.TryParseSort("size", out _).Should().BeFalse();
        FileQueryParsing.TryParseSort("--created_at", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("pending_upload", FileStatus.PendingUpload)]
    [InlineData("processing", FileStatus.Processing)]
    [InlineData("available", FileStatus.Available)]
    [InlineData("quarantined", FileStatus.Quarantined)]
    [InlineData("expired", FileStatus.Expired)]
    [InlineData("pending_deletion", FileStatus.PendingDeletion)]
    [InlineData("deleted", FileStatus.Deleted)]
    [InlineData("upload_failed", FileStatus.UploadFailed)]
    public void TryParseStatus_maps_known_values(string input, FileStatus expected)
    {
        FileQueryParsing.TryParseStatus(input, out var status).Should().BeTrue();
        status.Should().Be(expected);
    }

    [Fact]
    public void TryParseStatus_rejects_unknown_values()
    {
        FileQueryParsing.TryParseStatus("nope", out _).Should().BeFalse();
        FileQueryParsing.TryParseStatus("AVAILABLE", out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_round_trips_all_sort_key_fields()
    {
        var original = new FileListCursor(
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            "informe|Q3.pdf",
            42,
            Guid.Parse("0199c2b2-6c3a-7c11-9a9e-0d4b7a8f2c10"));

        var encoded = FileQueryParsing.EncodeCursor(original);

        FileQueryParsing.TryDecodeCursor(encoded, out var decoded).Should().BeTrue();
        decoded.Should().Be(original);
    }

    [Fact]
    public void Cursor_handles_null_sort_key_fields()
    {
        var original = new FileListCursor(null, null, null, Guid.Parse("0199c2b2-6c3a-7c11-9a9e-0d4b7a8f2c10"));

        var encoded = FileQueryParsing.EncodeCursor(original);

        FileQueryParsing.TryDecodeCursor(encoded, out var decoded).Should().BeTrue();
        decoded.Should().Be(original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Cursor_empty_value_decodes_to_null(string? input)
    {
        FileQueryParsing.TryDecodeCursor(input, out var decoded).Should().BeTrue();
        decoded.Should().BeNull();
    }

    [Theory]
    [InlineData("!!!not-base64url!!!")]
    [InlineData("eyJpZCI6Im5vdC1hLWd1aWQifQ==")]
    [InlineData("InZhbGlk")]
    public void Cursor_malformed_value_fails_gracefully(string input)
    {
        FileQueryParsing.TryDecodeCursor(input, out var decoded).Should().BeFalse();
        decoded.Should().BeNull();
    }
}
