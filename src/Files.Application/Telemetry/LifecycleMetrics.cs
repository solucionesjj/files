using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Files.Application.Telemetry;

/// <summary>
/// Lifecycle metrics for the retention/notification/deletion/reconciliation jobs (workflow §9,
/// NFR-011). Counters are additive; <c>job_last_success_timestamp_seconds</c> is an observable
/// gauge keyed by job name. Values are exported by OpenTelemetry when the meter is registered.
/// </summary>
public sealed class LifecycleMetrics : IDisposable
{
    public const string MeterName = "Files.Lifecycle";

    private readonly Meter _meter;
    private readonly Counter<long> _filesExpired;
    private readonly Counter<long> _filesDeleted;
    private readonly Counter<long> _filesDeletionFailures;
    private readonly Counter<long> _notificationDeliveryFailures;
    private readonly Counter<long> _reconcileMissingDb;
    private readonly Counter<long> _reconcileOrphanS3;
    private readonly Counter<long> _reconcileQuarantined;
    private readonly ConcurrentDictionary<string, long> _jobLastSuccess = new(StringComparer.Ordinal);

    public LifecycleMetrics()
    {
        _meter = new Meter(MeterName);
        _filesExpired = _meter.CreateCounter<long>("files_expired_total", description: "Files transitioned to pending_deletion by the expiration job.");
        _filesDeleted = _meter.CreateCounter<long>("files_deleted_total", description: "Files whose S3 object was physically deleted.");
        _filesDeletionFailures = _meter.CreateCounter<long>("files_deletion_failures_total", description: "Physical deletion failures by sanitized reason.");
        _notificationDeliveryFailures = _meter.CreateCounter<long>("notification_delivery_failures_total", description: "Notification delivery failures.");
        _reconcileMissingDb = _meter.CreateCounter<long>("reconcile_missing_db_total", description: "Available files whose S3 object was missing.");
        _reconcileOrphanS3 = _meter.CreateCounter<long>("reconcile_orphan_s3_total", description: "S3 objects found without a database row and moved/deleted.");
        _reconcileQuarantined = _meter.CreateCounter<long>("reconcile_quarantined_total", description: "Files quarantined by reconciliation.");

        _meter.CreateObservableGauge(
            "job_last_success_timestamp_seconds",
            () => _jobLastSuccess.Select(kv => new Measurement<long>(kv.Value, new KeyValuePair<string, object?>("job", kv.Key))),
            description: "Unix timestamp of the last successful run per job.");
    }

    public void RecordFileExpired(int count) => _filesExpired.Add(count);

    public void RecordFileDeleted() => _filesDeleted.Add(1);

    public void RecordDeletionFailure(string reason) =>
        _filesDeletionFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordNotificationDeliveryFailure() => _notificationDeliveryFailures.Add(1);

    public void RecordReconcileMissingDb(int count) => _reconcileMissingDb.Add(count);

    public void RecordReconcileOrphanS3(int count) => _reconcileOrphanS3.Add(count);

    public void RecordReconcileQuarantined(int count) => _reconcileQuarantined.Add(count);

    public void RecordJobSuccess(string job, DateTimeOffset at) =>
        _jobLastSuccess[job] = at.ToUnixTimeSeconds();

    public void Dispose() => _meter.Dispose();
}
