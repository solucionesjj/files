using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Files.IntegrationTests;

/// <summary>
/// Boots the real API host in-process and exercises the cross-cutting S1 surface: liveness,
/// public configuration, and RFC 9457 problem responses with a correlation ID. These tests do not
/// require PostgreSQL (the readiness probe is exercised by the Docker Compose health check).
/// </summary>
public class ApiSmokeTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=files;Username=files;Password=files";

    [Fact]
    public async Task Health_live_returns_200_ok()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Config_returns_public_non_sensitive_configuration()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var config = await response.Content.ReadFromJsonAsync<JsonElement>();
        config.GetProperty("service").GetString().Should().Be("files-api");
        config.GetProperty("retention").GetProperty("defaultDays").GetInt32().Should().Be(30);
        config.GetProperty("retention").GetProperty("maxDays").GetInt32().Should().Be(365);
        config.GetProperty("links").GetProperty("publicTtlDays").GetInt32().Should().Be(15);
        config.GetProperty("links").GetProperty("privateTtlDays").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_with_correlation_id()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.Should().Contain(header => header.Key == "X-Correlation-Id");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(404);
        problem.Extensions["code"]!.ToString().Should().Be("not_found");
    }

    [Fact]
    public async Task Requests_echo_or_generate_a_correlation_id()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "test-correlation-123");

        var response = await client.GetAsync("/health/live");

        response.Headers.Should().Contain(header =>
            header.Key == "X-Correlation-Id"
            && header.Value.Contains("test-correlation-123"));
    }

    // WebApplicationFactory applies its configuration overrides only after the entry point has
    // built the host, so pre-build validation (the fail-fast ConfigurationValidator) cannot see
    // them. Environment variables are read by WebApplication.CreateBuilder itself, so they are the
    // reliable way to satisfy the validator from a test.
    private static WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FilesDb", ConnectionString);
        Environment.SetEnvironmentVariable("Storage__StagingPath", "/tmp/files-staging");
        Environment.SetEnvironmentVariable("Auth__LinkKeyPepper", "integration-test-pepper");
        return new WebApplicationFactory<Program>();
    }
}
