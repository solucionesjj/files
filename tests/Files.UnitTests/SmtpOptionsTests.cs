using Files.Application.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Files.UnitTests;

public class SmtpOptionsTests
{
    [Fact]
    public void Reads_host_port_username_and_starttls_defaults()
    {
        var options = SmtpOptions.FromConfiguration(Build(
            "Smtp:Host", "smtp.office365.com",
            "Smtp:Username", "notificationsagent@ecs-la.com"));

        options.Host.Should().Be("smtp.office365.com");
        options.Port.Should().Be(587);
        options.Username.Should().Be("notificationsagent@ecs-la.com");
        options.UseStartTls.Should().BeTrue();
        options.HasCredentials.Should().BeFalse();
    }

    [Fact]
    public void Prefers_app_password_and_falls_back_to_legacy_password_key()
    {
        SmtpOptions.FromConfiguration(Build("Smtp:AppPassword", "app-secret"))
            .AppPassword.Should().Be("app-secret");

        SmtpOptions.FromConfiguration(Build("Smtp:Password", "legacy-secret"))
            .AppPassword.Should().Be("legacy-secret");
    }

    [Fact]
    public void Reads_from_address_and_default_locale()
    {
        var options = SmtpOptions.FromConfiguration(Build(
            "Notifications:FromAddress", "files@sacservices.cloud",
            "Notifications:DefaultLocale", "en-US",
            "Portal:BaseUrl", "https://files.sacservices.cloud"));

        options.FromAddress.Should().Be("files@sacservices.cloud");
        options.DefaultLocale.Should().Be("en-US");
        options.PortalUrl.Should().Be("https://files.sacservices.cloud");
    }

    [Fact]
    public void Default_locale_is_spanish()
    {
        SmtpOptions.FromConfiguration(Build()).DefaultLocale.Should().Be("es-CO");
    }

    private static IConfiguration Build(params string[] keyValues)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < keyValues.Length; i += 2)
        {
            values[keyValues[i]] = keyValues[i + 1];
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
