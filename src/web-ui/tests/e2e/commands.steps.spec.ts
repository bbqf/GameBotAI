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

  // Add a key step, then a home screen step. Each step is chosen by its action type.
  await page.getByLabel('Action type').selectOption('KeyInput');
  await page.getByLabel('Key identifier *').fill('Enter');
  await page.getByRole('button', { name: 'Add', exact: true }).click();

  await page.getByLabel('Action type').selectOption('GoToHomeScreen');
  await page.getByRole('button', { name: 'Add', exact: true }).click();

  const listItems = page.locator('.reorderable-list__item');
  await expect(listItems).toHaveCount(2);

  // The list reorders by drag. Drag the home step above the key step with the mouse.
  const handle = listItems.filter({ hasText: 'Go to home screen' }).getByRole('button', { name: 'Drag to reorder' });
  const target = await listItems.filter({ hasText: 'Key: Enter' }).boundingBox();
  const from = await handle.boundingBox();
  if (!from || !target) throw new Error('The steps are not on screen.');
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
  await page.mouse.down();
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2 - 8, { steps: 3 });
  // Move straight up, so the dragged step stays over the list.
  await page.mouse.move(from.x + from.width / 2, target.y + 2, { steps: 12 });
  await page.mouse.up();

  const finalItems = page.locator('.reorderable-list__item');
  await expect(finalItems).toHaveCount(2);
  await expect(finalItems.nth(0)).toContainText('Go to home screen');
  await expect(finalItems.nth(1)).toContainText('Key: Enter');
  await page.getByRole('button', { name: /^Save$/ }).click();

  await expect.poll(() => createPayload).not.toBeUndefined();

  expect(createPayload).toMatchObject({
    name: 'Playwright Cmd',
    steps: [
      { type: 'GoToHomeScreen', order: 0 },
      { type: 'KeyInput', order: 1, keyInput: { key: 'Enter' } },
    ],
  });
});