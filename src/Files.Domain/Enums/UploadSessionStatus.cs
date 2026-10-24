namespace Files.Domain.Enums;

/// <summary>Upload session state (column <c>upload_sessions.status</c>).</summary>
public enum UploadSessionStatus
{
    Initiated,
    Uploading,
    Received,
    Committing,
    Completed,
    Failed,
    Expired,
    Aborted,
}
