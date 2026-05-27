import { expect, test } from '@playwright/test';

const jsonHeaders = { 'Content-Type': 'application/json' };

test('command steps support add, reorder, delete and persist order on save', async ({ page }) => {
  const commands = [
    { id: 'c1', name: 'Existing Cmd', steps: [] },
  ];

  let createPayload: any;

  await page.route('**/api/commands', async (route) => {
    const method = route.request().method();
    if (method === 'GET') {
      return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(commands) });
    }

    if (method === 'POST') {
      createPayload = JSON.parse(route.request().postData() ?? '{}');
      const created = { id: 'c-new', ...createPayload };
      commands.push(created);
      return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(created) });
    }

    return route.fulfill({ status: 405, headers: jsonHeaders, body: '{}' });
  });

  await page.route('**/api/commands/*', async (route) => {
    const url = new URL(route.request().url());
    const id = url.pathname.split('/').pop() ?? '';
    const method = route.request().method();

    if (method === 'GET') {
      const found = commands.find((c) => c.id === id);
      return route.fulfill({ status: found ? 200 : 404, headers: jsonHeaders, body: JSON.stringify(found ?? {}) });
    }

    if (method === 'PUT') {
      const body = JSON.parse(route.request().postData() ?? '{}');
      const idx = commands.findIndex((c) => c.id === id);
      if (idx >= 0) commands[idx] = { ...commands[idx], ...body };
      return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(commands[idx] ?? body) });
    }

    return route.fulfill({ status: 204, headers: jsonHeaders, body: '' });
  });

  await page.route('**/api/action-types', async (route) => {
    return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify([]) });
  });

  await page.route('**/api/games', async (route) => {
    return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify([]) });
  });

  await page.goto('/');
  await page.getByRole('tab', { name: 'Authoring' }).click();
  await page.getByRole('tab', { name: 'Commands' }).click();
  await page.getByRole('button', { name: 'Create Command' }).click();

  await page.getByLabel('Name *').fill('Playwright Cmd');

  const commandDropdown = page.locator('#command-commands-dropdown');
  await commandDropdown.selectOption('c1');
  await page.getByRole('button', { name: 'Add command step' }).click();

  await page.getByLabel('Primitive tap image ID').fill('img-home');
  await page.getByLabel('Primitive confidence (0-1)').fill('0.92');
  await page.getByLabel('Primitive offset X').fill('3');
  await page.getByLabel('Primitive offset Y').fill('-2');
  await page.getByRole('button', { name: 'Add primitive tap step' }).click();

  const listItems = page.locator('.reorderable-list__item');
  await expect(listItems).toHaveCount(2);

  const primitiveItem = listItems.filter({ hasText: 'Primitive tap: img-home' });
  await primitiveItem.getByRole('button', { name: 'Move up' }).click();

  const finalItems = page.locator('.reorderable-list__item');
  await expect(finalItems).toHaveCount(2);
  await expect(finalItems.nth(0)).toContainText('Primitive tap: img-home');
  await expect(finalItems.nth(1)).toContainText('Command: Existing Cmd');

  await page.getByRole('button', { name: /^Save$/ }).click();

  await expect.poll(() => createPayload).not.toBeUndefined();

  expect(createPayload).toMatchObject({
    name: 'Playwright Cmd',
    steps: [
      {
        type: 'PrimitiveTap',
        order: 0,
        primitiveTap: {
          detectionTarget: {
            referenceImageId: 'img-home',
            confidence: 0.92,
            offsetX: 3,
            offsetY: -2,
          }
        }
      },
      { type: 'Command', targetId: 'c1', order: 1 },
    ],
  });
});
