using Files.Application.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Files.UnitTests;

public class ConfigurationValidatorTests
{
    [Fact]
    public void Validate_returns_no_errors_when_configuration_is_complete()
    {
        var errors = ConfigurationValidator.Validate(ValidConfig());

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_missing_database_connection_string()
    {
        var config = Substitute.For<IConfiguration>();
        config["Storage:StagingPath"].Returns("/var/tmp/files-staging");

        var errors = ConfigurationValidator.Validate(config);

        errors.Should().ContainSingle(e => e.Contains("ConnectionStrings__FilesDb", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_missing_staging_path()
    {
        var config = Substitute.For<IConfiguration>();
        config["ConnectionStrings:FilesDb"].Returns("Host=localhost;Database=files");

        var errors = ConfigurationValidator.Validate(config);

        errors.Should().ContainSingle(e => e.Contains("Storage__StagingPath", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_malformed_tenant_id()
    {
        var config = ValidConfig();
        config["Entra:TenantId"].Returns("not-a-guid");

        var errors = ConfigurationValidator.Validate(config);

        errors.Should().ContainSingle(e => e.Contains("Entra__TenantId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_malformed_otlp_endpoint()
    {
        var config = ValidConfig();
        config["OTEL_EXPORTER_OTLP_ENDPOINT"].Returns("not a url");

        var errors = ConfigurationValidator.Validate(config);

        errors.Should().ContainSingle(e => e.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", StringComparison.Ordinal));
    }

    [Fact]
    public void ThrowIfInvalid_throws_when_required_keys_are_missing()
    {
        var config = Substitute.For<IConfiguration>();

        var action = () => ConfigurationValidator.ThrowIfInvalid(config);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__FilesDb*");
    }

    private static IConfiguration ValidConfig()
    {
        var config = Substitute.For<IConfiguration>();
        config["ConnectionStrings:FilesDb"].Returns("Host=localhost;Database=files;Username=files;Password=files");
        config["Storage:StagingPath"].Returns("/var/tmp/files-staging");
        return config;
    }
}
