namespace Files.Application.Notifications;

/// <summary>An outbound email (ADR-0009). Never carries credentials, keys, or file contents.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>Result of an SMTP send attempt. Provider acceptance is not inbox delivery.</summary>
public sealed record EmailSendResult(
    bool Accepted,
    bool Permanent,
    string? ProviderMessageId,
    string? Error)
{
    public static EmailSendResult AcceptedResult(string? providerMessageId) =>
        new(true, false, providerMessageId, null);

    public static EmailSendResult TransientFailure(string error) =>
        new(false, false, null, error);

    public static EmailSendResult PermanentFailure(string error) =>
        new(false, true, null, error);
}

/// <summary>Outbound email seam; SMTP is the only implementation (POP is receive-only).</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
