import { expect, test } from '@playwright/test';

const jsonHeaders = { 'content-type': 'application/json' };

test('shows not found state when image missing', async ({ page }) => {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.toLowerCase();
    if (path === '/api/images') {
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify({ ids: ['ghost'] }) });
      return;
    }
    if (path === '/api/images/ghost/metadata' || path === '/api/images/ghost') {
      await route.fulfill({ status: 404, headers: jsonHeaders, body: JSON.stringify({ error: { code: 'not_found', message: 'Image not found' } }) });
      return;
    }
    await route.fulfill({ status: 200, headers: jsonHeaders, body: '{}' });
  });

  await page.goto('/?tab=Images');
  await page.getByRole('button', { name: 'ghost' }).click();
  await expect(page.getByText(/Image not found/i)).toBeVisible();
});
