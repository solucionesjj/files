namespace Files.Domain.Enums;

/// <summary>Lifecycle state of a stored file (column <c>files.status</c>).</summary>
public enum FileStatus
{
    PendingUpload,
    Processing,
    Available,
    Quarantined,
    Expired,
    PendingDeletion,
    Deleted,
    UploadFailed,
}
