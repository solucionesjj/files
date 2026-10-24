namespace Files.Domain.Enums;

/// <summary>Who requested a file deletion (column <c>files.deletion_requested_by_type</c>).</summary>
public enum DeletionRequesterType
{
    User,
    Integration,
    Admin,
    System,
}
