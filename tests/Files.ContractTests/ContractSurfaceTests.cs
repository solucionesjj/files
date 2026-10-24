using Files.Api;

namespace Files.ContractTests;

/// <summary>
/// Placeholder for OpenAPI conformance tests (T-08, S2/M1). The real contract is
/// <c>docs/04-api/openapi/files-api.v1.yaml</c>; conformance will compare generated endpoints and
/// DTOs against it. Until then, verify the public contract types are reachable from the API.
/// </summary>
public class ContractSurfaceTests
{
    [Fact]
    public void Public_config_contract_type_is_exposed()
    {
        Assert.Equal("Files.Api", typeof(PublicConfig).Assembly.GetName().Name);
    }
}
