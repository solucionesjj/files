using Files.Api;

namespace Files.ContractTests;

/// <summary>
/// Security surface conformance (T-04, FR-AUD-002, D-19): the audit contract is append-only (no
/// update/delete/upsert operations on audit or download-event resources), and no admin operation
/// returns file content. The runtime enforcement lives in
/// <c>tests/Files.IntegrationTests/Upload/AuthorizationMatrixTests.cs</c>.
/// </summary>
public class SecuritySurfaceTests
{
    private static readonly string Contract = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files-api.v1.yaml"));

    [Fact]
    public void Public_config_contract_type_is_exposed()
    {
        Assert.Equal("Files.Api", typeof(PublicConfig).Assembly.GetName().Name);
    }

    [Fact]
    public void Audit_and_download_event_resources_are_read_only()
    {
        // Append-only (FR-AUD-002): only the list (GET) operations exist.
        Assert.Contains("operationId: listAuditEvents", Contract);
        Assert.Contains("operationId: listDownloadEvents", Contract);

        // No mutating operation may be declared for audit or download-event resources.
        Assert.DoesNotContain("createAuditEvent", Contract);
        Assert.DoesNotContain("updateAuditEvent", Contract);
        Assert.DoesNotContain("deleteAuditEvent", Contract);
        Assert.DoesNotContain("purgeAuditEvent", Contract);
        Assert.DoesNotContain("updateDownloadEvent", Contract);
        Assert.DoesNotContain("deleteDownloadEvent", Contract);
    }

    [Fact]
    public void Admin_read_surface_is_declared_and_deferred_mutations_are_not()
    {
        // S13/M7: the read-only admin/auditor surface is implemented contract-first (FR-ADM-005/006/008).
        Assert.Contains("operationId: listAuditEvents", Contract);
        Assert.Contains("operationId: listDownloadEvents", Contract);
        Assert.Contains("operationId: getUsage", Contract);
        Assert.Contains("operationId: getPlatformSettings", Contract);

        // The mutating admin operations were explicitly deferred (owner decision) and removed from
        // the contract so it does not claim an unimplemented surface.
        Assert.DoesNotContain("operationId: requestAdminFileDeletion", Contract);
        Assert.DoesNotContain("operationId: createFileGrant", Contract);
        Assert.DoesNotContain("operationId: revokeFileGrant", Contract);
        Assert.DoesNotContain("operationId: listFileGrants", Contract);
        Assert.DoesNotContain("operationId: updatePlatformSettings", Contract);
    }

    [Fact]
    public void No_admin_operation_returns_file_content()
    {
        // D-19: admins manage metadata/policy/legal hold but never read content. The contract must
        // not declare an admin content/download operation (the only content paths are the anonymous
        // grant stream and the integration content endpoint).
        Assert.DoesNotContain("operationId: downloadFileAsAdmin", Contract);
        Assert.DoesNotContain("operationId: getAdminFileContent", Contract);
        Assert.DoesNotContain("operationId: downloadAdminFile", Contract);
        Assert.DoesNotContain("/admin/files/{fileId}/content", Contract);
    }

    [Fact]
    public void Integration_surface_is_still_declared()
    {
        Assert.Contains("operationId: downloadIntegrationFile", Contract);
        Assert.Contains("operationId: getIntegrationFileAvailability", Contract);
    }
}
