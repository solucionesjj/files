namespace Files.Domain.Entities;

/// <summary>Versioned email template (<c>email_templates</c> table), keyed by key + locale (ADR-0009).</summary>
public sealed class EmailTemplate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Key { get; set; } = string.Empty;

    public string Locale { get; set; } = "es-CO";

    public string Subject { get; set; } = string.Empty;

    public string BodyHtml { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
