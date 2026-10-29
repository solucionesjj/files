using Files.Application.Storage;
using FluentAssertions;

namespace Files.UnitTests;

public class StorageErrorsTests
{
    [Theory]
    [InlineData("SlowDown", null, "throttled")]
    [InlineData("Throttling", null, "throttled")]
    [InlineData("RequestTimeout", null, "timeout")]
    [InlineData("InternalError", null, "server_error")]
    [InlineData("ServiceUnavailable", null, "service_unavailable")]
    [InlineData("AccessDenied", null, "access_denied")]
    [InlineData("NoSuchBucket", null, "no_such_bucket")]
    [InlineData(null, 503, "server_error")]
    [InlineData(null, 429, "throttled")]
    [InlineData(null, 408, "timeout")]
    [InlineData("UnknownError", 400, "s3_error")]
    public void SanitizeCode_maps_provider_codes_to_stable_non_leaking_tokens(
        string? providerCode, int? status, string expected)
    {
        StorageErrors.SanitizeCode(providerCode, status).Should().Be(expected);
    }

    [Theory]
    [InlineData("SlowDown", null, true)]
    [InlineData("Throttling", null, true)]
    [InlineData("RequestTimeout", null, true)]
    [InlineData("InternalError", null, true)]
    [InlineData("ServiceUnavailable", null, true)]
    [InlineData(null, 500, true)]
    [InlineData(null, 429, true)]
    [InlineData(null, 408, true)]
    [InlineData("AccessDenied", null, false)]
    [InlineData("NoSuchBucket", null, false)]
    [InlineData("UnknownError", 400, false)]
    public void IsRetryable_distinguishes_transient_from_permanent_failures(
        string? providerCode, int? status, bool expected)
    {
        StorageErrors.IsRetryable(providerCode, status).Should().Be(expected);
    }
}
