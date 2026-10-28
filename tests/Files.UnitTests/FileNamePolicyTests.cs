using Files.Application.Storage;
using FluentAssertions;

namespace Files.UnitTests;

public class FileNamePolicyTests
{
    [Theory]
    [InlineData("informe Q3.pdf")]
    [InlineData("a")]
    [InlineData("résumé (final).docx")]
    public void Accepts_ordinary_file_names(string fileName)
    {
        FileNamePolicy.IsValid(fileName).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_file_names(string? fileName)
    {
        FileNamePolicy.IsValid(fileName).Should().BeFalse();
    }

    [Theory]
    [InlineData("dir/file.txt")]
    [InlineData(@"dir\file.txt")]
    [InlineData("line\nbreak.txt")]
    [InlineData("nul\0byte.txt")]
    public void Rejects_path_separators_and_control_characters(string fileName)
    {
        FileNamePolicy.IsValid(fileName).Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_name_longer_than_the_maximum()
    {
        var fileName = new string('a', 513);
        FileNamePolicy.IsValid(fileName).Should().BeFalse();
    }
}
