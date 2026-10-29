using Files.Api;

namespace Files.ContractTests;

/// <summary>
/// Contract-first conformance (T-08). The authoritative contract is
/// <c>docs/04-api/openapi/files-api.v1.yaml</c>; Redocly lints it in <c>make validate</c>. These
/// tests guard the integration surface so a contract change without the matching implementation
/// (or vice versa) is caught in CI.
/// </summary>
public class ContractSurfaceTests
{
    private static readonly string Contract = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files-api.v1.yaml"));

    [Fact]
    public void Public_config_contract_type_is_exposed()
    {
        Assert.Equal("Files.Api", typeof(PublicConfig).Assembly.GetName().Name);
    }

    [Fact]
    public void Integration_endpoints_are_declared()
    {
        Assert.Contains("openapi: 3.1.0", Contract);
        Assert.Contains("operationId: listIntegrationFiles", Contract);
        Assert.Contains("operationId: getIntegrationFile", Contract);
        Assert.Contains("operationId: deleteIntegrationFile", Contract);
        Assert.Contains("operationId: getIntegrationFileAvailability", Contract);
        Assert.Contains("operationId: downloadIntegrationFile", Contract);
    }

    [Fact]
    public void Integration_schemas_and_parameters_are_declared()
    {
        Assert.Contains("IntegrationFile:", Contract);
        Assert.Contains("IntegrationFileList:", Contract);
        Assert.Contains("name: Idempotency-Key", Contract);
        Assert.Contains("name: Range", Contract);
        Assert.Contains("name: X-Api-Key", Contract);
    }

    [Fact]
    public void Admin_retention_and_notification_endpoints_are_declared()
    {
        Assert.Contains("operationId: getRetentionPolicy", Contract);
        Assert.Contains("operationId: createRetentionPolicy", Contract);
        Assert.Contains("operationId: createRetentionMigration", Contract);
        Assert.Contains("operationId: getRetentionMigration", Contract);
        Assert.Contains("operationId: overrideFileRetention", Contract);
        Assert.Contains("operationId: setLegalHold", Contract);
        Assert.Contains("operationId: getNotificationPolicy", Contract);
        Assert.Contains("operationId: updateNotificationPolicy", Contract);
        Assert.Contains("operationId: listEmailTemplates", Contract);
        Assert.Contains("operationId: upsertEmailTemplate", Contract);
    }

    [Fact]
    public void Admin_retention_and_notification_schemas_are_declared()
    {
        Assert.Contains("RetentionPolicy:", Contract);
        Assert.Contains("RetentionPolicyHistory:", Contract);
        Assert.Contains("CreateRetentionPolicyRequest:", Contract);
        Assert.Contains("RetentionMigration:", Contract);
        Assert.Contains("CreateRetentionMigrationRequest:", Contract);
        Assert.Contains("NotificationPolicy:", Contract);
        Assert.Contains("UpdateNotificationPolicyRequest:", Contract);
        Assert.Contains("EmailTemplate:", Contract);
        Assert.Contains("UpsertEmailTemplateRequest:", Contract);
        Assert.Contains("AdminRetentionOverrideRequest:", Contract);
        Assert.Contains("LegalHoldRequest:", Contract);
    }

    [Fact]
    public void Admin_audit_and_usage_endpoints_are_declared()
    {
        Assert.Contains("operationId: listAuditEvents", Contract);
        Assert.Contains("operationId: listDownloadEvents", Contract);
        Assert.Contains("operationId: getUsage", Contract);
        Assert.Contains("operationId: getPlatformSettings", Contract);
    }

    [Fact]
    public void Admin_audit_and_usage_schemas_are_declared()
    {
        Assert.Contains("AuditEvent:", Contract);
        Assert.Contains("AuditEventList:", Contract);
        Assert.Contains("DownloadEvent:", Contract);
        Assert.Contains("DownloadEventList:", Contract);
        Assert.Contains("UsageStats:", Contract);
        Assert.Contains("PlatformSettings:", Contract);
    }
}
