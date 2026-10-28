import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { API_BASE, expectSignedIn, fileBody, mockApi, mockConfig, seedEntraSession } from './helpers';

test.beforeEach(async ({ page }) => {
  await mockConfig(page);
  await seedEntraSession(page);
  await mockApi(page);
});

test('signs in with the mocked Entra session and lists files', async ({ page }) => {
  await page.goto('/files');
  await expectSignedIn(page);
  await expect(page.getByText('report.pdf').first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Descargar' }).first()).toBeVisible();
});

test('searching triggers a filtered list request', async ({ page }) => {
  await page.goto('/files');
  await expect(page.getByText('report.pdf').first()).toBeVisible();

  const search = page.waitForRequest((r) => r.url().includes('/v1/portal/files') && r.url().includes('q=quarterly'));
  await page.getByPlaceholder('Buscar archivos…').fill('quarterly');
  await search;
});

test('renames, retags, and changes retention through the edit dialog with If-Match', async ({ page }) => {
  let patchBody: unknown;
  let ifMatchHeader: string | null = null;
  await page.route(`${API_BASE}/v1/portal/files/file-1`, async (route) => {
    if (route.request().method() !== 'PATCH') {
      return route.fallback();
    }
    patchBody = route.request().postDataJSON();
    ifMatchHeader = route.request().headers()['if-match'] ?? null;
    return route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(fileBody({ fileName: 'renamed.pdf', tags: ['legal'], version: 2, etag: '"2"' })),
    });
  });

  await page.goto('/files');
  await page.getByRole('button', { name: 'Editar' }).first().click();

  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nombre').fill('renamed.pdf');
  await dialog.getByLabel('Etiquetas').fill('legal');
  await dialog.getByLabel('Retención (días)').fill('365');
  await dialog.getByLabel('Motivo para ampliar la retención').fill('contract review until Q1');
  await dialog.getByRole('button', { name: 'Guardar' }).click();

  await expect(dialog).toBeHidden();
  expect(ifMatchHeader).toBe('"1"');
  expect(patchBody).toMatchObject({
    fileName: 'renamed.pdf',
    tags: ['legal'],
    retention: { mode: 'finite', days: 365, reason: 'contract review until Q1' },
  });
});

test('deleting a file requires confirmation and calls the delete endpoint', async ({ page }) => {
  let deleteCalled = false;
  await page.route(`${API_BASE}/v1/portal/files/file-1`, async (route) => {
    if (route.request().method() !== 'DELETE') {
      return route.fallback();
    }
    deleteCalled = true;
    return route.fulfill({ status: 204, body: '' });
  });

  await page.goto('/files');
  await page.getByRole('button', { name: 'Eliminar' }).first().click();

  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Eliminar' }).click();

  await expect(dialog).toBeHidden();
  expect(deleteCalled).toBe(true);
});

test('downloading issues a grant through the API (no fetch-to-Blob)', async ({ page }) => {
  await page.route('**/v1/download-grants/**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/octet-stream',
      headers: { 'Content-Disposition': 'attachment; filename="report.pdf"' },
      body: Buffer.from('fake-bytes'),
    }),
  );

  await page.goto('/files');
  const downloadRequest = page.waitForRequest(
    (r) => r.method() === 'POST' && r.url().includes('/v1/portal/files/file-1/downloads'),
  );
  await page.getByRole('button', { name: 'Descargar' }).first().click();
  await downloadRequest;
});

test('has no accessibility violations on the list and detail routes', async ({ page }) => {
  await page.goto('/files');
  await expect(page.getByText('report.pdf').first()).toBeVisible();
  const listScan = await new AxeBuilder({ page }).analyze();
  expect(listScan.violations).toEqual([]);

  await page.getByText('report.pdf').first().click();
  await expect(page.getByRole('heading', { name: 'report.pdf' })).toBeVisible();
  const detailScan = await new AxeBuilder({ page }).analyze();
  expect(detailScan.violations).toEqual([]);
});

test('file detail renders metadata and management actions', async ({ page }) => {
  await page.goto('/files/file-1');
  await expectSignedIn(page);
  await expect(page.getByRole('heading', { name: 'report.pdf' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Descargar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Editar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Eliminar' })).toBeVisible();
});
