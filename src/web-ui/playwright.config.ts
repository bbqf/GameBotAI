import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  timeout: 60_000,
  expect: {
    timeout: 5_000,
  },
  reporter: 'list',
  fullyParallel: true,
  // Many parallel browsers slow the local servers and cause load time-outs.
  workers: 2,
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
      testIgnore: /mobile\.spec\.ts/,
    },
    {
      name: 'mobile',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 360, height: 640 },
        hasTouch: true,
        isMobile: true,
        // The phone tests run against the built UI, because the first-screen time test needs it.
        baseURL: 'http://localhost:4174',
      },
      testMatch: /mobile\.spec\.ts/,
    }
  ],
  webServer: [
    {
      command: 'npm run dev -- --host --port 4173',
      url: 'http://localhost:4173',
      reuseExistingServer: true,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'npm run build && npm run preview -- --host --port 4174 --strictPort',
      url: 'http://localhost:4174',
      reuseExistingServer: true,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
