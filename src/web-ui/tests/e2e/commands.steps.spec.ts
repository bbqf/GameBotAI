import { expect, test } from '@playwright/test';

const jsonHeaders = { 'Content-Type': 'application/json' };

test('command steps support add, reorder, delete and persist order on save', async ({ page }) => {
  const actions = [
    { id: 'a1', name: 'Action One', description: 'first action' },
    { id: 'a2', name: 'Action Two', description: 'second action' },
  ];

  const commands = [
    { id: 'c1', name: 'Existing Cmd', steps: [] },
  ];

  let createPayload: any;

  await page.route('**/api/actions', async (route) => {
    if (route.request().method() !== 'GET') {
      return route.fulfill({ status: 405, headers: jsonHeaders, body: '{}' });
    }
    return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(actions) });
  });

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

  await page.goto('/');
  await page.getByRole('tab', { name: 'Commands' }).click();
  await page.getByRole('button', { name: 'Create Command' }).click();

  await page.getByLabel('Name *').fill('Playwright Cmd');

  const actionDropdown = page.locator('#command-actions-dropdown');
  await actionDropdown.selectOption('a1');
  await page.getByRole('button', { name: 'Add action step' }).click();

  await actionDropdown.selectOption('a2');
  await page.getByRole('button', { name: 'Add action step' }).click();

  const commandDropdown = page.locator('#command-commands-dropdown');
  await commandDropdown.selectOption('c1');
  await page.getByRole('button', { name: 'Add command step' }).click();

  const listItems = page.locator('.reorderable-list__item');
  await expect(listItems).toHaveCount(3);

  const commandItem = listItems.filter({ hasText: 'Command: Existing Cmd' });
  await commandItem.getByRole('button', { name: 'Move up' }).click();
  await commandItem.getByRole('button', { name: 'Move up' }).click();

  const actionOneItem = listItems.filter({ hasText: 'Action: Action One' });
  await actionOneItem.getByRole('button', { name: 'Delete' }).click();

  const finalItems = page.locator('.reorderable-list__item');
  await expect(finalItems).toHaveCount(2);
  await expect(finalItems.nth(0)).toContainText('Command: Existing Cmd');
  await expect(finalItems.nth(1)).toContainText('Action: Action Two');

  await page.getByRole('button', { name: /^Save$/ }).click();

  await expect.poll(() => createPayload).not.toBeUndefined();

  expect(createPayload).toMatchObject({
    name: 'Playwright Cmd',
    steps: [
      { type: 'Command', targetId: 'c1', order: 0 },
      { type: 'Action', targetId: 'a2', order: 1 },
    ],
  });
});
