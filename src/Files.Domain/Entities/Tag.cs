using Files.Domain.Enums;

namespace Files.Domain.Entities;

/// <summary>Reusable, case-insensitive label (<c>tags</c> table).</summary>
public sealed class Tag
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public TagScope Scope { get; set; } = TagScope.Global;

    public Guid? OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
