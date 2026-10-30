import { defineConfig, devices } from '@playwright/test';

// The e2e run gets its own port. 5000 is the dev default, but on macOS AirPlay Receiver
// listens there and `reuseExistingServer` would silently test against it.
const port = Number(process.env.E2E_PORT ?? 5080);
const baseURL = `http://localhost:${port}`;

export default defineConfig({
  testDir: './tests',
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `dotnet run --project ../backend/src/InvoiceApp.Api --urls ${baseURL}`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
