using Files.Application.Configuration;
using Files.Application.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Files.Infrastructure.Email;

/// <summary>
/// Outbound email over SMTP AUTH 587 with STARTTLS (ADR-0009). POP is receive-only and is never
/// used. Credentials come from <see cref="SmtpOptions"/> and are never logged or returned.
/// </summary>
public sealed partial class SmtpEmailSender(SmtpOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return EmailSendResult.PermanentFailure("smtp_not_configured");
        }

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(
                options.Host,
                options.Port,
                options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                ct);

            if (options.HasCredentials)
            {
                await client.AuthenticateAsync(options.Username!, options.AppPassword!, ct);
            }

            var mime = new MimeMessage
            {
                MessageId = $"{Guid.NewGuid():N}@files.local",
            };
            mime.From.Add(new MailboxAddress(options.FromDisplayName, options.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new BodyBuilder
            {
                TextBody = message.TextBody,
                HtmlBody = message.HtmlBody,
            }.ToMessageBody();

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);

            return EmailSendResult.AcceptedResult(mime.MessageId);
        }
        catch (SmtpCommandException ex)
        {
            Log.CommandRejected(logger, (int)ex.StatusCode);
            return (int)ex.StatusCode >= 500
                ? EmailSendResult.PermanentFailure("smtp_rejected")
                : EmailSendResult.TransientFailure("smtp_rejected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.SendError(logger, ex.GetType().Name);
            return EmailSendResult.TransientFailure(ex.GetType().Name);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP command rejected (status {StatusCode})")]
        public static partial void CommandRejected(ILogger logger, int statusCode);

        [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP send failed ({Error})")]
        public static partial void SendError(ILogger logger, string error);
    }
}
