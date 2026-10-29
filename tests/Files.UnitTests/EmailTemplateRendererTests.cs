using Files.Application.Notifications;
using Files.Domain.Entities;
using FluentAssertions;

namespace Files.UnitTests;

public class EmailTemplateRendererTests
{
    private static readonly EmailVariables Variables = new(
        FileName: "informe <trimestral> & \"final\".pdf",
        ExpiresAt: new DateTimeOffset(2027, 1, 21, 21, 0, 0, TimeSpan.Zero),
        IntervalDays: 7,
        OwnerName: "Jane <Doe> & Co",
        PortalUrl: "https://files.sacservices.cloud",
        FileId: Guid.NewGuid().ToString("D"));

    [Fact]
    public void Renders_subject_text_and_escaped_html_with_the_allowlist()
    {
        var template = new EmailTemplate
        {
            Key = "file.expiring",
            Locale = "es-CO",
            Subject = "Tu archivo \"{{fileName}}\" vence en {{intervalDays}} días",
            BodyText = "Hola {{ownerName}}: {{fileName}} vence el {{expiresAt}} ({{intervalDays}} días). {{portalUrl}}",
            BodyHtml = "<p>Hola {{ownerName}}: <strong>{{fileName}}</strong> vence el {{expiresAt}}. <a href=\"{{portalUrl}}\">{{portalUrl}}</a></p>",
        };

        var rendered = EmailTemplateRenderer.Render(template, Variables);

        rendered.Should().NotBeNull();
        rendered!.Subject.Should().Be("Tu archivo \"informe <trimestral> & \"final\".pdf\" vence en 7 días");
        rendered.TextBody.Should().Contain("informe <trimestral> & \"final\".pdf");
        // HTML body escapes the filename/owner name but keeps the URL as-is (escaped ampersands become &amp;).
        rendered.HtmlBody.Should().Contain("informe &lt;trimestral&gt; &amp; &quot;final&quot;.pdf");
        rendered.HtmlBody.Should().Contain("Jane &lt;Doe&gt; &amp; Co");
        rendered.HtmlBody.Should().Contain("https://files.sacservices.cloud");
    }

    [Fact]
    public void Localizes_the_expiry_date_per_locale()
    {
        var es = new EmailTemplate { Key = "k", Locale = "es-CO", Subject = "{{expiresAt}}", BodyText = "{{expiresAt}}", BodyHtml = "{{expiresAt}}" };
        var en = new EmailTemplate { Key = "k", Locale = "en-US", Subject = "{{expiresAt}}", BodyText = "{{expiresAt}}", BodyHtml = "{{expiresAt}}" };

        // es-CO short date is dd/MM/yyyy; en-US is M/d/yyyy. Both render, just assert they differ.
        var esRendered = EmailTemplateRenderer.Render(es, Variables)!;
        var enRendered = EmailTemplateRenderer.Render(en, Variables)!;
        esRendered.Subject.Should().NotBe(enRendered.Subject);
    }

    [Fact]
    public void Unknown_placeholder_fails_rendering()
    {
        var template = new EmailTemplate
        {
            Key = "file.expiring",
            Locale = "es-CO",
            Subject = "{{fileName}}",
            BodyText = "{{fileName}} {{secretToken}}",
            BodyHtml = "<p>{{fileName}}</p>",
        };

        EmailTemplateRenderer.Render(template, Variables).Should().BeNull();
    }

    [Fact]
    public void File_id_variable_is_supported()
    {
        var template = new EmailTemplate
        {
            Key = "file.expiring",
            Locale = "en-US",
            Subject = "File {{fileId}}",
            BodyText = "File {{fileId}} expires {{expiresAt}}",
            BodyHtml = "<p>File {{fileId}}</p>",
        };

        var rendered = EmailTemplateRenderer.Render(template, Variables);

        rendered.Should().NotBeNull();
        rendered!.Subject.Should().Be($"File {Variables.FileId}");
    }
}
