namespace Files.Domain.Enums;

/// <summary>Where a file's content type came from (column <c>files.content_type_source</c>).</summary>
public enum ContentTypeSource
{
    Declared,
    Sniffed,
    Verified,
}
