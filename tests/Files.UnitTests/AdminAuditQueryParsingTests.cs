using Files.Application.Admin;
using FluentAssertions;

namespace Files.UnitTests;

public class AdminAuditQueryParsingTests
{
    [Fact]
    public void Cursor_round_trips_occurred_at_and_id()
    {
        var cursor = new AuditEventCursor(
            new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero),
            Guid.Parse("11111111-2222-3333-4444-555555555555"));

        var encoded = AdminAuditQueryParsing.EncodeCursor(cursor);
        encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");

        AdminAuditQueryParsing.TryDecodeCursor(encoded, out var decoded).Should().BeTrue();
        decoded.Should().Be(cursor);
    }

    [Fact]
    public void Empty_cursor_decodes_to_null()
    {
        AdminAuditQueryParsing.TryDecodeCursor(null, out var nullCursor).Should().BeTrue();
        nullCursor.Should().BeNull();

        AdminAuditQueryParsing.TryDecodeCursor("", out var emptyCursor).Should().BeTrue();
        emptyCursor.Should().BeNull();
    }

    [Fact]
    public void Malformed_cursor_is_rejected()
    {
        AdminAuditQueryParsing.TryDecodeCursor("not-base64!", out _).Should().BeFalse();
        AdminAuditQueryParsing.TryDecodeCursor("eyJub3QiOiJjdXJzb3IifQ==", out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_with_empty_id_is_rejected()
    {
        var encoded = AdminAuditQueryParsing.EncodeCursor(new AuditEventCursor(DateTimeOffset.UtcNow, Guid.Empty));
        AdminAuditQueryParsing.TryDecodeCursor(encoded, out var cursor).Should().BeFalse();
        cursor.Should().BeNull();
    }
}
