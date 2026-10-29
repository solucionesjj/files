import { expect, type Page } from '@playwright/test';
import type { FileMetadata } from '../src/app/core/models/file-metadata';
import type { Link, LinkCreated, LinkPublicInfo } from '../src/app/core/models/download-links';

/** Fake Entra tenant/client used only to make the portal's MSAL layer treat itself as configured. */
export const E2E_ENTRA = {
  tenantId: '11111111-1111-1111-1111-111111111111',
  clientId: '22222222-2222-2222-2222-222222222222',
  objectId: '33333333-3333-3333-3333-333333333333',
  environment: 'login.microsoftonline.com',
} as const;

export const API_BASE = 'http://localhost:8080';

export function apiScope(): string {
  return `api://${E2E_ENTRA.clientId}/Files.Access`;
}

/** Serves the test runtime config (never contains secrets). */
export async function mockConfig(page: Page): Promise<void> {
  await page.route('**/assets/config.json', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        environment: 'e2e',
        apiBaseUrl: API_BASE,
        tenantId: E2E_ENTRA.tenantId,
        clientId: E2E_ENTRA.clientId,
        apiScope: apiScope(),
        redirectUri: 'http://localhost:4200/auth/callback',
        features: { publicLinks: true },
      }),
    }),
  );
}

/**
 * Seeds the MSAL v5 sessionStorage cache with a fake account + access token so the portal signs in
 * without a real Entra tenant (the "mocked Entra" from the testing strategy).
 */
export async function seedEntraSession(page: Page): Promise<void> {
  await page.addInitScript((entra) => {
    const { tenantId, clientId, objectId, environment } = entra;
    const scope = `api://${clientId}/Files.Access`;
    const homeAccountId = `${objectId}.${tenantId}`;
    const lower = (value: string) => value.toLowerCase();

    const accountKey = lower(`msal.3|${homeAccountId}|${environment}|${tenantId}`);
    const accessTokenKey = lower(
      `msal.3|${homeAccountId}|${environment}|accesstoken|${clientId}|${tenantId}|${scope}|`,
    );

    const account = {
      homeAccountId,
      environment,
      realm: tenantId,
      localAccountId: objectId,
      username: 'owner@example.com',
      name: 'Test Owner',
      authorityType: 'MSSTS',
    };

    const nowSeconds = Math.floor(Date.now() / 1000);
    const accessToken = {
      homeAccountId,
      environment,
      credentialType: 'AccessToken',
      clientId,
      realm: tenantId,
      target: scope,
      tokenType: 'Bearer',
      secret: 'e2e-access-token',
      expiresOn: String(nowSeconds + 3600),
      cachedAt: String(nowSeconds),
      extendedExpiresOn: String(nowSeconds + 7200),
    };

    sessionStorage.setItem('msal.3.account.keys', JSON.stringify([accountKey]));
    sessionStorage.setItem(accountKey, JSON.stringify(account));
    sessionStorage.setItem(
      `msal.3.token.keys.${clientId}`,
      JSON.stringify({ idToken: [], accessToken: [accessTokenKey], refreshToken: [] }),
    );
    sessionStorage.setItem(accessTokenKey, JSON.stringify(accessToken));
  }, E2E_ENTRA as unknown as Record<string, string>);
}

/** Minimal owner identity returned by `GET /v1/me`. */
export function meBody(): unknown {
  return {
    userId: E2E_ENTRA.objectId,
    tenantId: E2E_ENTRA.tenantId,
    email: 'owner@example.com',
    displayName: 'Test Owner',
    roles: ['user'],
    permissions: [],
  };
}

export function fileBody(overrides: Partial<FileMetadata> = {}): FileMetadata {
  return {
    id: 'file-1',
    fileName: 'report.pdf',
    sizeBytes: 1024,
    contentType: 'application/pdf',
    status: 'available',
    accessMode: 'owner_only',
    retention: { mode: 'finite', days: 30, expiresAt: null, reason: null },
    tags: [],
    downloads: { count: 0, lastAt: null },
    scanStatus: 'clean',
    legalHold: false,
    legalHoldReason: null,
    legalHoldReviewAt: null,
    owner: { type: 'human', displayName: 'Test Owner' },
    quarantineReason: null,
    createdAt: '2026-10-28T12:00:00Z',
    updatedAt: '2026-10-28T12:00:00Z',
    availableAt: '2026-10-28T12:00:00Z',
    deletedAt: null,
    version: 1,
    etag: '"1"',
    ...overrides,
  };
}

export function linkBody(overrides: Partial<Link> = {}): Link {
  return {
    id: 'link-1',
    fileId: 'file-1',
    accessMode: 'public',
    status: 'active',
    createdAt: '2026-10-28T12:00:00Z',
    expiresAt: '2026-11-12T12:00:00Z',
    maxDownloads: null,
    successfulDownloads: 0,
    reservedDownloads: 0,
    revokedAt: null,
    createdBy: { type: 'user', displayName: 'Test Owner' },
    ...overrides,
  };
}

export function linkCreatedBody(overrides: Partial<LinkCreated> = {}): LinkCreated {
  return {
    ...linkBody(),
    url: '/d/abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRST',
    token: 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRST',
    downloadKey: 'Ab3xY9Qz',
    ...overrides,
  };
}

export function publicLinkBody(overrides: Partial<LinkPublicInfo> = {}): LinkPublicInfo {
  return {
    fileName: 'report.pdf',
    sizeBytes: 1024,
    contentType: 'application/pdf',
    requiresKey: true,
    requiresAuthentication: false,
    expiresAt: '2026-11-12T12:00:00Z',
    remainingDownloads: 5,
    ownerDisplayName: 'Test Owner',
    ...overrides,
  };
}

/** Mocks the Files API surface the portal exercises. */
export async function mockApi(page: Page, files: FileMetadata[] = [fileBody()]): Promise<void> {
  await page.route((url) => url.origin === new URL(API_BASE).origin && url.pathname === '/v1/me', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(meBody()) }),
  );

  await page.route(
    (url) => url.origin === new URL(API_BASE).origin && url.pathname === '/v1/portal/files',
    (route) => {
      if (route.request().method() !== 'GET') {
        return route.fallback();
      }
      return route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: files, nextCursor: null, hasMore: false }),
      });
    },
  );

  // File detail (GET). PATCH/DELETE are overridden by individual tests (registered later and win).
  await page.route(
    (url) => url.origin === new URL(API_BASE).origin && /^\/v1\/portal\/files\/[^/]+$/.test(url.pathname),
    (route) => {
      if (route.request().method() !== 'GET') {
        return route.fallback();
      }
      const id = route.request().url().split('/').pop() ?? 'file-1';
      return route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify(fileBody({ id })),
      });
    },
  );

  await page.route(
    (url) =>
      url.origin === new URL(API_BASE).origin && /^\/v1\/portal\/files\/[^/]+\/downloads$/.test(url.pathname),
    (route) =>
      route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({
          grantToken: 'e2e-grant-token',
          expiresAt: new Date(Date.now() + 120_000).toISOString(),
          downloadUrl: '/v1/download-grants/e2e-grant-token',
        }),
      }),
  );

  // Link list on the file detail page (empty by default; individual tests override creation).
  await page.route(
    (url) =>
      url.origin === new URL(API_BASE).origin && /^\/v1\/portal\/files\/[^/]+\/links$/.test(url.pathname),
    (route) => {
      if (route.request().method() !== 'GET') {
        return route.fallback();
      }
      return route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [], nextCursor: null, hasMore: false }),
      });
    },
  );
}

/** Asserts the signed-in shell (name + sign-out) rendered, proving the mocked Entra session works. */
export async function expectSignedIn(page: Page): Promise<void> {
  await expect(page.getByText('Test Owner')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
}
