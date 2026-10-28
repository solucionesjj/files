namespace Files.Domain.Entities;

/// <summary>Join row between a file and a tag (<c>file_tags</c> table).</summary>
public sealed class FileTag
{
    public Guid FileId { get; set; }

    public Guid TagId { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Referenced tag (for name-based filtering).</summary>
    public Tag Tag { get; set; } = null!;
}
