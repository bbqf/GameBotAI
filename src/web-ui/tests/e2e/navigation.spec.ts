import { expect, test } from '@playwright/test';

const jsonHeaders = { 'content-type': 'application/json' };

const stubApiRoutes = async (page: import('@playwright/test').Page) => {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.toLowerCase();
    const listBodies: Record<string, unknown> = {
      '/api/commands': [],
      '/api/sequences': [],
      '/api/games': [],
      '/api/triggers': [],
      '/api/config': { host: '', token: '' },
    };
    const body = path in listBodies ? listBodies[path] : {};
    await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(body) });
  });
};

test.beforeEach(async ({ page }) => {
  await stubApiRoutes(page);
});

test('tabs switch between areas and hide triggers', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('tab', { name: 'Authoring', exact: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Configuration', exact: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Execution', exact: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: /Triggers/i })).toHaveCount(0);

  await page.getByRole('tab', { name: 'Configuration', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Configuration' })).toBeVisible();

  await page.getByRole('tab', { name: 'Execution', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Execution' })).toBeVisible();
});

test('bottom tab bar replaces the tabs on small screens', async ({ page }) => {
  await page.setViewportSize({ width: 480, height: 800 });
  await page.goto('/');
  await expect(page.getByRole('tab', { name: 'Authoring', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: /More/ }).click();
  await page.getByRole('button', { name: 'Configuration' }).click();
  await expect(page.getByRole('heading', { name: 'Configuration' })).toBeVisible();
});

test('authoring items reachable in one click from landing', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('tab', { name: 'Commands', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Commands' })).toBeVisible();
});

test('configuration inputs editable without authoring content', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('tab', { name: 'Configuration', exact: true }).click();
  // The Backend Connection section is closed on a wide screen. Open it first.
  await page.getByText('Backend Connection').click();
  const baseUrl = page.getByLabel('API Base URL');
  const token = page.getByLabel('Bearer Token');
  await baseUrl.fill('http://localhost:5000');
  await token.fill('abc123');
  await expect(baseUrl).toHaveValue('http://localhost:5000');
  await expect(token).toHaveValue('abc123');
});

test('execution placeholder loads and returns to authoring', async ({ page }) => {
  await page.goto('/execution');
  await expect(page.getByRole('heading', { name: 'Execution' })).toBeVisible();
  await page.getByRole('tab', { name: 'Authoring', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Authoring' })).toBeVisible();
});