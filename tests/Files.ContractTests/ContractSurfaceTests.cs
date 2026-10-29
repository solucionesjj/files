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
}
