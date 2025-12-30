import { expect, test } from '@playwright/test';
import { Buffer } from 'buffer';

const jsonHeaders = { 'content-type': 'application/json' };
const tinyPng = Buffer.from('89504E470D0A1A0A', 'hex');

test('detect runs with defaults and shows table + cap note', async ({ page }) => {
  let detectBody: any = null;

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.toLowerCase();

    if (path === '/api/images' && route.request().method() === 'GET') {
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify({ ids: ['tpl'] }) });
      return;
    }

    if (path === '/api/images/tpl/metadata') {
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify({ id: 'tpl', contentType: 'image/png', sizeBytes: 10 }) });
      return;
    }

    if (path === '/api/images/tpl') {
      await route.fulfill({ status: 200, headers: { 'content-type': 'image/png' }, body: tinyPng });
      return;
    }

    if (path === '/api/images/detect') {
      detectBody = route.request().postDataJSON();
      await route.fulfill({
        status: 200,
        headers: jsonHeaders,
        body: JSON.stringify({
          matches: [{ templateId: 'tpl', score: 0.92, x: 0.1, y: 0.2, width: 0.3, height: 0.4, overlap: 0.2 }],
          limitsHit: true
        })
      });
      return;
    }

    await route.fulfill({ status: 200, headers: jsonHeaders, body: '{}' });
  });

  await page.goto('/?tab=Images');
  await page.getByRole('button', { name: 'tpl' }).click();
  await page.getByRole('button', { name: 'Run Detect' }).click();

  expect(detectBody).toEqual({ referenceImageId: 'tpl', threshold: 0.86, maxResults: 1, overlap: 0.1 });

  const table = page.getByRole('table', { name: 'Detection results' });
  await expect(table).toBeVisible();
  await expect(table.getByRole('row')).toHaveCount(2); // header + 1 row
  await expect(table.getByRole('cell', { name: 'tpl' })).toBeVisible();
  await expect(table.getByRole('cell', { name: '0.92' })).toBeVisible();
  await expect(page.getByText('Max results reached; more matches may exist.')).toBeVisible();
});
