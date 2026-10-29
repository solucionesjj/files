namespace Files.Application.Integrations;

/// <summary>
/// Pure read-access predicate for integration file retrieval (FR-INT-005 / FR-M2M-007): ownership
/// OR an active, non-expired, non-revoked <c>integration_file_grants</c> row. Foreign files resolve
/// to <c>false</c>, which callers surface as a uniform 404 (no existence oracle, ADR-0012).
/// </summary>
public static class IntegrationFileAccess
{
    public static bool CanRead(Guid? ownerIntegrationId, Guid integrationId, bool hasActiveGrant) =>
        ownerIntegrationId == integrationId || hasActiveGrant;
}
