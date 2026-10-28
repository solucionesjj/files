import { defineConfig } from '@playwright/test';

/**
 * Portal E2E (S7). The Angular app is served by `ng serve`; Entra is mocked by seeding the MSAL
 * sessionStorage cache and the Files API is mocked with `page.route` (the real API behavior is
 * covered by the backend integration tests). Accessibility runs axe-core on the key routes.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:4200',
    headless: true,
    viewport: { width: 1280, height: 800 },
  },
  webServer: {
    command: 'npm run start -- --configuration development --port 4200',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
});
