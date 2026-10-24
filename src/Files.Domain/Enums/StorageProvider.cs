namespace Files.Domain.Enums;

/// <summary>Object storage backend (column <c>files.storage_provider</c>).</summary>
public enum StorageProvider
{
    S3,
    Minio,
}
