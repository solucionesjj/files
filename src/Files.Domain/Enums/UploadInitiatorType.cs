namespace Files.Domain.Enums;

/// <summary>Kind of actor that opened an upload session (column <c>upload_sessions.initiator_type</c>).</summary>
public enum UploadInitiatorType
{
    User,
    Integration,
}
