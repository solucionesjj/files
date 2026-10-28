using Files.Application.Storage;
using FluentAssertions;

namespace Files.UnitTests;

public class ContentTypePolicyTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("application/vnd.api+json")]
    public void Accepts_well_formed_media_types(string contentType)
    {
        ContentTypePolicy.IsValid(contentType).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-slash")]
    [InlineData("application/")]
    [InlineData("/pdf")]
    [InlineData("has space/pdf")]
    public void Rejects_malformed_media_types(string? contentType)
    {
        ContentTypePolicy.IsValid(contentType).Should().BeFalse();
    }
}
