import { expect, test } from '@playwright/test';
import { Buffer } from 'buffer';

const jsonHeaders = { 'content-type': 'application/json' };

test('uploads and navigates to detail', async ({ page }) => {
  let hasUploaded = false;

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.toLowerCase();

    if (path === '/api/images' && route.request().method() === 'GET') {
      const ids = hasUploaded ? ['fresh-id'] : [];
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify({ ids }) });
      return;
    }

    if (path === '/api/images' && route.request().method() === 'POST') {
      hasUploaded = true;
      await route.fulfill({ status: 201, headers: jsonHeaders, body: JSON.stringify({ id: 'fresh-id', overwrite: false }) });
      return;
    }

    if (path === '/api/images/fresh-id/metadata') {
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify({ id: 'fresh-id', contentType: 'image/png', sizeBytes: 10 }) });
      return;
    }

    if (path === '/api/images/fresh-id') {
      await route.fulfill({ status: 200, headers: { 'content-type': 'image/png' }, body: Buffer.from('89504E470D0A1A0A', 'hex') });
      return;
    }

    await route.fulfill({ status: 200, headers: jsonHeaders, body: '{}' });
  });

  await page.goto('/?tab=Images');

  const fileChooserPromise = page.waitForEvent('filechooser');
  await page.getByLabel('File (PNG/JPEG, ≤10 MB)').click();
  const chooser = await fileChooserPromise;
  const tmp = await page.context().storageState({});
  const blob = Buffer.from('89504E470D0A1A0A', 'hex');
  await chooser.setFiles({ name: 'tiny.png', mimeType: 'image/png', buffer: blob });
  await page.getByLabel('Image ID').fill('fresh-id');
  await page.getByRole('button', { name: 'Upload' }).click();

  await expect(page.getByTestId('selected-image-id')).toHaveText('fresh-id');
});
