import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import {
  API_BASE,
  expectSignedIn,
  linkBody,
  linkCreatedBody,
  mockApi,
  mockConfig,
  publicLinkBody,
  seedEntraSession,
} from './helpers';

const TOKEN = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRST';

test.beforeEach(async ({ page }) => {
  await mockConfig(page);
  await seedEntraSession(page);
  await mockApi(page);
});

test('creates a link, shows the key once, and the public page downloads it', async ({ page }) => {
  // Override the detail link list to reflect the link after creation.
  await page.route(
    (url) => url.origin === new URL(API_BASE).origin && url.pathname === '/v1/portal/files/file-1/links',
    async (route) => {
      if (route.request().method() === 'POST') {
        return route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify(linkCreatedBody()),
        });
      }
      return route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [linkBody()], nextCursor: null, hasMore: false }),
      });
    },
  );

  await page.goto('/files/file-1');
  await expectSignedIn(page);

  await page.getByRole('button', { name: 'Crear enlace' }).click();

  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Crear enlace' }).click();

  // The key is shown exactly once in the creation dialog.
  await expect(dialog.getByLabel('Clave de descarga')).toHaveValue('Ab3xY9Qz');
  await expect(dialog.getByLabel('Enlace')).toHaveValue(new RegExp(`/d/${TOKEN}$`));

  await dialog.getByRole('button', { name: 'Listo' }).click();
  await expect(dialog).toBeHidden();

  // The link list now shows the active link.
  await expect(page.getByRole('table').getByText('Activo')).toBeVisible();

  // Open the public landing page, enter the key, and stream the download.
  await page.route(`${API_BASE}/v1/public/download-links/${TOKEN}`, (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(publicLinkBody()) }),
  );
  await page.route(`${API_BASE}/v1/public/download-links/${TOKEN}/downloads`, (route) =>
    route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        grantToken: 'e2e-grant-token',
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        downloadUrl: '/v1/download-grants/e2e-grant-token',
      }),
    }),
  );
  await page.route('**/v1/download-grants/**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/octet-stream',
      headers: { 'Content-Disposition': 'attachment; filename="report.pdf"' },
      body: Buffer.from('fake-bytes'),
    }),
  );

  await page.goto(`/d/${TOKEN}`);
  await expect(page.getByText('report.pdf')).toBeVisible();

  const redeem = page.waitForRequest(
    (r) => r.method() === 'POST' && r.url().includes(`/v1/public/download-links/${TOKEN}/downloads`),
  );
  await page.getByLabel('Clave de descarga (8 caracteres)').fill('Ab3xY9Qz');
  await page.getByRole('button', { name: 'Descargar' }).click();
  await redeem;
});

test('public landing page and link management have no accessibility violations', async ({ page }) => {
  await page.route(`${API_BASE}/v1/public/download-links/${TOKEN}`, (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(publicLinkBody()) }),
  );
  await page.route(
    (url) => url.origin === new URL(API_BASE).origin && url.pathname === '/v1/portal/files/file-1/links',
    (route) =>
      route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [linkBody()], nextCursor: null, hasMore: false }),
      }),
  );

  await page.goto(`/d/${TOKEN}`);
  await expect(page.getByText('report.pdf')).toBeVisible();
  const publicScan = await new AxeBuilder({ page }).analyze();
  expect(publicScan.violations).toEqual([]);

  await page.goto('/files/file-1');
  await expect(page.getByRole('table').getByText('Activo')).toBeVisible();
  const detailScan = await new AxeBuilder({ page }).analyze();
  expect(detailScan.violations).toEqual([]);
});
