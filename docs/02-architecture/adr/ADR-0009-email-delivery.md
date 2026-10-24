# ADR-0009 — Email delivery via the Microsoft 365 mailbox over SMTP AUTH

- **Status:** Accepted (OQ-01 resolved; mailbox `notificationsagent@ecs-la.com`; password pending in `.env`)
- **Date:** 2026-10-23
- **Deciders:** Architecture, IT

## Context

The approved sender is a Microsoft 365 mailbox configured with **POP**. POP3 is receive-only and
cannot deliver expiration notifications. Microsoft 365 exposes SMTP AUTH on `smtp.office365.com`
(port 587, STARTTLS) for the same mailbox; Graph `sendMail` is an alternative if tenant policy
blocks SMTP AUTH. The product needs authenticated sending, retries, delivery status, and no
credentials in logs.

## Decision

- **Sending:** `SmtpEmailSender` using MailKit over `smtp.office365.com:587` with STARTTLS, the
  full mailbox address as username, and a password/app password stored in `/opt/files/.env`
  (`Smtp__Password`, mode 0600). The mailbox must have **SMTP AUTH enabled**; POP remains enabled
  only for inbound mail and is not used by Files.
- **From:** `Notifications__FromAddress` (e.g., `files@<company-domain>`), display name "Files".
- **TLS:** required (`Smtp__UseStartTls=true`, port 587). Certificate validation is never disabled.
- **Graph fallback:** `Microsoft.Graph` `sendMail` with application `Mail.Send` restricted to the
  same mailbox is implemented behind `IEmailSender`; it is the fallback if the tenant enforces
  OAuth-only SMTP or MFA blocks the mailbox password.
- **Templates:** `email_templates` keyed by key+locale; strict variable allowlist (`fileName`,
  `expiresAt`, `ownerName`, `portalUrl`, `intervalDays`, `fileId`); HTML-escaped; no file content,
  links/keys, or credentials.
- **Delivery status:** `queued → sending → accepted → delivered|bounced|failed`.
  SMTP acceptance means the server accepted the message, **not** inbox delivery. NDR/message-trace
  ingestion can later update delivered/bounced; until then the UI must show "accepted".
- **Retries:** transient errors (4xx, timeouts, 429) → exponential backoff with jitter, max 5
  attempts; permanent 5xx auth/recipient errors → `failed` + alert. One send per
  `notification_events` row; stuck `sending` >10 min is reclaimed.
- **Rate:** batch 50/minute; respect `Retry-After`; one connection per batch, STARTTLS only.
- **Suppression:** indefinite retention, deleted files, and files whose expiry moved.

## Security notes

- SMTP credentials are secrets: never logged, never in URLs, never in the repository. Rotate the
  mailbox password/app password periodically and after any suspicion.
- Prefer an app password scoped to SMTP or OAuth2/Graph if the tenant requires MFA or disables
  basic SMTP AUTH; document the chosen path in `.env` (`Notifications__Provider=Smtp|Graph`).
- SPF/DKIM/DMARC remain the company's mail-domain configuration; Files sends through Microsoft 365
  so the existing domain alignment applies.
- The SMTP connection uses TLS and validates the server certificate.

## Consequences

- No additional email provider is required; the approved mailbox is reused.
- If Microsoft disables SMTP AUTH for the tenant, email sending fails until Graph is enabled.
  This is a known operational dependency (runbook email failures).
- Password rotation requires updating `.env` and restarting the worker; no code change.
- POP settings do not affect outbound mail; enabling/disabling POP has no effect on notifications.

## Alternatives considered

- **POP3 for sending:** impossible (receive-only).
- **Microsoft Graph `sendMail` only:** cleaner (no mailbox password) but requires an Entra app
  permission and Exchange application access policy; kept as fallback.
- **Third-party relay (SES/SendGrid):** rejected for vendor/data-residency reasons in this phase.
