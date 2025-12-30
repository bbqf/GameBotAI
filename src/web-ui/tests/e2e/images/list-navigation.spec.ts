import { expect, test } from '@playwright/test';

const jsonHeaders = { 'content-type': 'application/json' };

const stubApiRoutes = async (page: import('@playwright/test').Page) => {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.toLowerCase();

    if (path === '/api/images') {
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify({ ids: ['snow-owl', 'stage-door'] }) });
      return;
    }

    const listBodies: Record<string, unknown> = {
      '/api/actions': [],
      '/api/commands': [],
      '/api/sequences': [],
      '/api/games': [],
      '/api/triggers': [],
      '/api/config': { host: '', token: '' }
    };

    const body = path in listBodies ? listBodies[path] : {};
    await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(body) });
  });
};

test.beforeEach(async ({ page }) => {
  await stubApiRoutes(page);
});

test('lists images and opens detail view', async ({ page }) => {
  await page.goto('/?tab=Images');

  await expect(page.getByRole('heading', { name: 'Images' })).toBeVisible();
  await expect(page.getByRole('table', { name: /Images table/i })).toBeVisible();

  const firstId = page.getByRole('button', { name: 'snow-owl' });
  await expect(firstId).toBeVisible();
  await firstId.click();

  await expect(page.getByRole('heading', { name: 'Image Detail' })).toBeVisible();
  await expect(page.getByTestId('selected-image-id')).toHaveText('snow-owl');
  await expect(page.getByRole('table', { name: /Images table/i })).toBeVisible();
});
