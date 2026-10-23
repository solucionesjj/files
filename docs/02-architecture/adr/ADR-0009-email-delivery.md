# ADR-0009 — Email delivery through Microsoft Graph (not POP3)

- **Status:** Provisional — blocked on OQ-01 (mailbox/app permission)
- **Date:** 2026-10-23
- **Deciders:** Architecture, IT

## Context

The source states "Email: MS365 Pop account". POP3 is a receive-only protocol and cannot send
expiration notifications. The system needs authenticated, auditable sending from a company
mailbox, with retries and acceptance tracking, and must distinguish provider acceptance from
confirmed delivery.

## Decision

- Primary provider: Microsoft Graph `POST /users/{sender}/sendMail` using an Entra app
  registration with application permission `Mail.Send`, restricted by an **application access
  policy** (Exchange PowerShell) to the single sending mailbox.
- Credentials: client credentials with a certificate (preferred) or secret stored in the runtime
  secret store; no SMTP password. TLS is implicit.
- Sender: `Notifications__FromAddress` (e.g., `files-noreply@company.com`), display name "Files".
- Templates: stored in `email_templates` (key, locale, subject, HTML, text, version) and seeded
  with defaults. Rendering uses a strict allowlist of variables (`fileName`, `expiresAt`,
  `ownerName`, `portalUrl`, `fileId`) and HTML-escapes values. No file content, keys, tokens, or
  integration secrets are ever interpolated.
- Delivery statuses: `queued → sending → accepted → delivered|bounced|failed`.
  - `accepted`: Graph returned 202 (provider accepted).
  - `delivered`/`bounced`: derived from NDR/Message Trace ingestion or Graph change
    notifications (optional follow-up). Until implemented, the UI must display "accepted" and
    must not claim confirmed delivery.
- Retries: transient errors (429, 5xx, network) exponential backoff with jitter, max 5 attempts,
  next attempt ≥1 min. Permanent errors (400 invalid recipient, 403 permission) stop retrying and
  mark `failed`; metrics + alert.
- Suppression: indefinite files, deleted files, and files whose expiry moved are not sent.
  `notification_events` unique key prevents duplicates.
- Rate: batches of 50 per minute; per-mailbox Graph throttling respected; 429 honors
  `Retry-After`.
- Fallback (only if Graph is not approved): SMTP AUTH with OAuth2 to `smtp.office365.com:587`
  (never basic auth). POP settings are irrelevant and must not be used for sending.

## Consequences

- Requires an Entra app permission and Exchange application access policy; this is an
  operational prerequisite for M6/production.
- Graph outages delay notifications but never block expiration/deletion.
- Delivery confirmation beyond "accepted" requires extra work (message trace); the product shows
  the distinction honestly.
- The email path is behind `IEmailSender`, so an SES/SendGrid adapter can be added without
  domain changes.

## Alternatives considered

- SMTP with username/password: rejected (basic auth deprecated, secret handling, less auditable).
- POP3: technically impossible for sending.
- Third-party transactional email: not chosen for data-residency/vendor reasons (future option).
