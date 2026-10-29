using Files.Application.Storage;
using FluentAssertions;

namespace Files.UnitTests;

/// <summary>
/// RFC 6266/5987 <c>Content-Disposition</c> behavior (T-05): the ASCII fallback never allows a
/// header break or CRLF injection, and the <c>filename*</c> extended value preserves the original
/// name via percent-encoding (FR-UP-011, ADR-0003 §8).
/// </summary>
public class ContentDispositionTests
{
    [Fact]
    public void Ascii_name_is_quoted_verbatim_and_encoded_identically()
    {
        ContentDisposition.Build("report.pdf").Should()
            .Be("attachment; filename=\"report.pdf\"; filename*=UTF-8''report.pdf");
    }

    [Fact]
    public void Unicode_name_uses_underscored_ascii_fallback_and_percent_encoded_extended_value()
    {
        ContentDisposition.Build("résumé.pdf").Should()
            .Be("attachment; filename=\"r_sum_.pdf\"; filename*=UTF-8''r%C3%A9sum%C3%A9.pdf");
    }

    [Fact]
    public void Quotes_and_backslashes_are_replaced_in_the_ascii_fallback()
    {
        ContentDisposition.Build("a\"b\\c.txt").Should()
            .Be("attachment; filename=\"a_b_c.txt\"; filename*=UTF-8''a%22b%5Cc.txt");
    }

    [Theory]
    [InlineData("a\r\nb.txt")]
    [InlineData("a\nb.txt")]
    [InlineData("a\tb.txt")]
    [InlineData("\u202Eevil.txt")]
    public void Control_characters_and_bidi_marks_are_replaced_not_injected(string name)
    {
        var header = ContentDisposition.Build(name);

        header.Should().NotContainAny("\r", "\n", "\t", "\u202E");
        header.Should().StartWith("attachment; filename=\"");
        header.Should().Contain("filename*=UTF-8''");
    }

    [Fact]
    public void Spaces_are_preserved_in_the_ascii_fallback()
    {
        ContentDisposition.Build("my file.txt").Should()
            .Be("attachment; filename=\"my file.txt\"; filename*=UTF-8''my%20file.txt");
    }
}
