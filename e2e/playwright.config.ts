import { defineConfig } from '@playwright/test';

// End-to-end tests run the REAL app locally: the start script builds the
// Angular frontend, serves it from the API's wwwroot (the production layout)
// and starts the API on http://localhost:5000. Nothing here touches the
// live site — the live site is Francesco's, these tests run on localhost.
export default defineConfig({
  testDir: './tests',
  timeout: 120_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:5000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: 'bash start-app.sh',
    url: 'http://localhost:5000/health',
    timeout: 300_000,
    reuseExistingServer: false,
  },
});
