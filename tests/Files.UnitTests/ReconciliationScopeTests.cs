using Files.Application.Deletion;
using FluentAssertions;

namespace Files.UnitTests;

public class ReconciliationScopeTests
{
    [Theory]
    [InlineData("f/018f8c6a-0000-7000-8000-000000000000", true)]
    [InlineData("staging/part-1", false)]
    [InlineData("orphan/f/x", false)]
    [InlineData("backups/postgres/dump.sql", false)]
    public void IsFileObject_matches_only_committed_file_keys(string key, bool expected)
    {
        ReconciliationScope.IsFileObject(key).Should().Be(expected);
    }

    [Theory]
    [InlineData("staging/part-1", true)]
    [InlineData("f/x", false)]
    [InlineData("orphan/staging/part-1", false)]
    public void IsStagingObject_matches_only_staging_keys(string key, bool expected)
    {
        ReconciliationScope.IsStagingObject(key).Should().Be(expected);
    }

    [Fact]
    public void OrphanDestinationKey_keeps_the_relative_path_under_orphan_prefix()
    {
        ReconciliationScope.OrphanDestinationKey("f/abc").Should().Be("orphan/f/abc");
        ReconciliationScope.OrphanDestinationKey("staging/part-1").Should().Be("orphan/staging/part-1");
    }

    [Fact]
    public void Fingerprint_never_returns_the_full_key()
    {
        var key = "f/018f8c6a-0000-7000-8000-000000000000";
        var fingerprint = ReconciliationScope.Fingerprint(key);

        fingerprint.Should().NotBe(key);
        fingerprint.Should().HaveLength(16); // 8 bytes = 16 hex chars
        fingerprint.Should().MatchRegex("^[0-9a-f]{16}$");

        // Deterministic: the same key always yields the same fingerprint.
        ReconciliationScope.Fingerprint(key).Should().Be(fingerprint);
    }
}
