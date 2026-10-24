namespace Files.Domain.Enums;

/// <summary>Malware scan result for a file (column <c>files.scan_status</c>).</summary>
public enum ScanStatus
{
    NotScanned,
    Pending,
    Clean,
    Infected,
    Failed,
}
