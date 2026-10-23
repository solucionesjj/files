# Frontend Design (Files Portal)

## 1. Stack and workspace

- Angular LTS (standalone components, signals, functional guards/interceptors).
- Bootstrap 5.3 (grid/utilities/components) + Bootstrap Icons; no custom design system in v1.
- `@azure/msal-angular` + `@azure/msal-browser` for Entra ID (Authorization Code + PKCE).
- TypeScript strict, ESLint + Prettier, SCSS.
- Testing: Vitest/Jest unit tests (Angular default), Playwright E2E.
- Workspace lives in `web/`; build output is static and served by Caddy on the EC2 host.

```
web/src/app/
  core/            # auth, http interceptors, config, error handling, telemetry
  shared/          # ui components, pipes, directives, validators
  features/
    files/         # list, detail, upload, tags, retention
    links/         # link management, public link page, key entry
    stats/         # download statistics
    admin/         # integrations, policies, notifications, audit, usage
  layout/          # shell, nav, breadcrumbs, toasts
  app.routes.ts
```

State: component signals + feature services; no global store in v1. Server state is fetched per
route with caching disabled for sensitive data (`Cache-Control: no-store`).

## 2. Routes

| Route | Guard | Purpose |
|---|---|---|
| `/` | MsalGuard | Redirect to `/files`. |
| `/files` | MsalGuard | File list, search, filters, pagination. |
| `/files/:id` | MsalGuard | File detail, metadata, links, stats. |
| `/links` | MsalGuard | Links created by me (active/revoked/expired). |
| `/admin/integrations` | MsalGuard + role `Files.Admin` | Integration and credential management. |
| `/admin/policies` | role `Files.Admin` | Retention, notifications, quotas, templates. |
| `/admin/audit` | role `Files.Admin` or `Files.Auditor` | Audit and download events. |
| `/admin/usage` | role `Files.Admin` or `Files.Auditor` | Storage, downloads, quota usage. |
| `/d/:token` | none | Public link landing: file summary + key entry. |
| `/d/:token` (private) | MsalGuard after metadata | Redirects to login if the link requires it. |
| `/access-denied` | none | 403 page. |
| `/**` | none | 404. |

Lazy-load every feature route. Preload the shell only.

## 3. Authentication

- `MsalConfig`: authority `https://login.microsoftonline.com/{tenantId}`, client ID from
  runtime config, redirect URI `${origin}/auth/callback`, scopes
  `["api://{apiClientId}/Files.Access"]`.
- Cache location: `sessionStorage` (no refresh tokens in JS); `storeAuthStateInCookie: false`.
- `MsalInterceptor` attaches tokens for the API origin only. A second interceptor handles API
  errors (401 → acquire token silently; if it fails, redirect to login).
- `MsalRedirectComponent` at `/auth/callback`.
- Role guard reads `idTokenClaims.roles` (falls back to `account.idTokenClaims`); the API is
  still authoritative.
- Sign-out clears MSAL cache and redirects to the Entra logout endpoint with
  `postLogoutRedirectUri`.
- Runtime config (`/assets/config.json`) supplies `apiBaseUrl`, `tenantId`, `clientId`,
  `apiScope`, `environment`, `features.publicLinks`. It must not contain secrets.

## 4. API access

- One typed client per feature (`FilesApi`, `LinksApi`, `AdminApi`) over `HttpClient`, with
  DTO interfaces generated/maintained from the OpenAPI contract.
- All requests use `withCredentials: false`; no cookies.
- Errors are normalized to a `Problem` model (`code`, `detail`, `traceId`); toasts show a
  translated message plus a trace ID; validation errors map to fields.
- Pagination: cursor kept in the URL query string so back/forward and refresh preserve state.
- **Downloads use a two-step grant** (browser-safe, no bearer header on navigation):
  1. `POST /v1/portal/files/{id}/downloads` (or private/public link grant) returns
     `grantToken`.
  2. Navigate to `/v1/download-grants/{grantToken}`; the API streams the file.
  Never fetch a 5 GiB file into a Blob. Grants are 120 s, single-use, and `no-store`.
- **Uploads** use chunked `PUT`s to the Files API with XHR (progress events), 2 chunks in flight,
  retry per chunk with exponential backoff, and resume via `GET .../upload-sessions/{id}`. Chunk
  size comes from the session response (16 MiB default). The browser never receives an S3 URL,
  credential, or CORS configuration; it never talks to S3.
- The client splits files locally, sends `X-Chunk-SHA256` for each chunk when practical, and on
  `complete` polls the file until `available`/`processing`.

## 5. Key screens and UX rules

### File list
- Columns: name, size, type, status, retention/expiry, tags, downloads, created.
- Search debounced 300 ms; filters in a collapsible panel; sortable headers; page size 25/50/100.
- Status badges: Available, Processing, Quarantined, Expired, Pending deletion, Deleted.
- Expiry: relative ("in 12 days") + absolute tooltip; "Indefinite" for null expiry.
- Empty state explains drag-and-drop upload.
- Row actions: download, copy private link, manage links, edit, delete (confirm + type-to-confirm
  for files with downloads).

### Upload
- Drag-and-drop or picker; per-file size/type validation before the session request; progress
  per file and overall; cancel; retry failed parts; "remaining time" estimate.
- On completion, poll `GET /v1/portal/files/{id}` until `available` or `processing` (scan) with
  backoff; show scan status.
- If the tab closes mid-upload, the session remains open for its TTL; reopening the page can
  resume using the received-chunk list. If it expires, the staging file is removed and the UI
  explains that the upload must restart.

### Links
- Create link dialog: type (private/public), optional expiry, optional max downloads; public
  requires the policy to be enabled.
- After creation, show **once**: link URL and the 8-character key, with copy buttons and a
  warning that the key cannot be recovered. Never place the key in the URL or clipboard
  automatically.
- Link list shows status, expiry, downloads used/limit, creator, created; revoke with confirm.
- Public landing page: filename, size, owner display name (configurable), expiry, key field
  (autocomplete off, `inputmode="text"`, maxlength 8, paste allowed), generic error text.
- Private landing: the anonymous metadata call returns only `requiresAuthentication`; the SPA
  redirects to sign-in, then fetches full metadata from the authenticated link endpoint.
- Set `Referrer-Policy: no-referrer` for `/d/*` and avoid third-party resources on that route.

### Admin
- Integrations: list with status/permissions/last use; create/edit; disable with confirmation;
  credentials with prefix, creator, expiry, last used; "Issue key" shows once.
- Policies: retention form (default, max, user override, indefinite), notification intervals
  (chips, ordered), templates with preview, public links toggle, quotas.
- Audit: filter by actor, file, action, date range; detail drawer with metadata; no content.
- Usage: storage by owner type, downloads by day, top files, quota consumption.

## 6. Accessibility and responsiveness

- WCAG 2.1 AA: labels for every input, visible focus, 4.5:1 contrast, no color-only status,
  aria-live for upload progress and toasts, modal focus trap, skip link.
- Keyboard: full flow for list, upload, link creation, key entry.
- Responsive breakpoints: ≥1200 full table; 768–1199 condensed columns; <768 card list with
  actions in an overflow menu.
- Respect `prefers-reduced-motion`.

## 7. Security rules

- Never store access tokens in `localStorage`; use MSAL `sessionStorage`.
- Never render filenames with `innerHTML`; Angular interpolation only. Sanitize display names
  server-side; the UI trusts the API's sanitization for `Content-Disposition`.
- CSP (served by Caddy): `default-src 'self'`; `connect-src` API + Entra; `img-src 'self' data:`;
  no `unsafe-inline` (Angular build with strict CSP may require nonces for styles — configure at
  deploy). No S3 origin is allowed.
- No secrets in `config.json`; no analytics/trackers on `/d/*`.
- Validate `returnUrl` against an allowlist to prevent open redirects.

## 8. Performance budgets

- Initial JS ≤ 250 KiB gzip; LCP ≤ 2.5 s on a 4G profile; route chunks lazy.
- Upload/download progress must remain responsive (no main-thread hashing of large files).
- File list virtualizes beyond 100 rows? No — server pagination only; 25–100 rows are fine.
- Images/icons via SVG sprite; Bootstrap imported selectively (grid, forms, buttons, modals).

## 9. Testing

- Unit: guards, interceptors, upload orchestration (mocked XHR), error mapping, pipes.
- Component: list filters, link dialog (key shown once), key entry validation.
- E2E (Playwright) against the real API + PostgreSQL + LocalStack S3 + mocked Entra (or test
  tenant): sign-in, chunked upload/resume, search, download, create/redeem public link, revoke,
  admin policy change.
- Accessibility: axe-core scan on key routes in CI.
- Contract: DTOs type-checked against the OpenAPI schema (openapi-typescript) in CI.

## 10. i18n

Spanish (`es-CO`) is the initial UI language. Use Angular i18n (`@angular/localize`) with
`$localize` for user-facing strings; dates/numbers via `Intl` with `es-CO`; keep identifiers
and code comments in English. Add `en-US` only if requested (OQ-14).
