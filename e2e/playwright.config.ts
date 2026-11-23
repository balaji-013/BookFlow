import { defineConfig } from '@playwright/test';
// Runs against a live stack: BASE_URL=http://localhost:8080 npx playwright test
export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  retries: 0,
  use: { baseURL: process.env.BASE_URL || 'http://localhost:8080', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  reporter: [['list'], ['html', { open: 'never' }]],
});
