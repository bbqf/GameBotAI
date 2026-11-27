import { expect, test, Page } from '@playwright/test';
import { mockNotificationsApi } from './helpers/notificationsMock';

/**
 * Feature 203: the Notifications area on the shared UI components. A jsdom test cannot measure layout, so this
 * test runs in a real browser. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

const headerWidth = async (page: Page, table: string, name: string) => {
  const box = await page.locator(`table.${table} th`, { hasText: new RegExp(`^${name}$`, 'i') }).boundingBox();
  return box!.width;
};

const open = async (page: Page) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  const state = await mockNotificationsApi(page);
  await page.goto('/?area=notifications');
  await expect(page.locator('[data-testid^="target-row-"]')).toHaveCount(3);
  return state;
};

const menuDelete = async (page: Page, name: string) => {
  const more = page.getByRole('button', { name: `More actions for ${name}` });
  // The menu closes on a scroll, so the row must be in view before the click.
  await more.scrollIntoViewIfNeeded();
  await more.click();
  await page.getByRole('menuitem', { name: /Delete/ }).click();
};

test.describe('Notifications layout (desktop)', () => {
  test('target list has fixed column widths, row heights, and chips', async ({ page }) => {
    await open(page);

    near(await headerWidth(page, 'notification-target-list', 'Type'), 120);
    near(await headerWidth(page, 'notification-target-list', 'Enabled'), 132);
    near(await headerWidth(page, 'notification-target-list', 'Chat ID'), 200);
    near(await headerWidth(page, 'notification-target-list', 'Bot token'), 160);
    near(await headerWidth(page, 'notification-target-list', 'Actions'), 148);

    const rows = page.locator('[data-testid^="target-row-"]');
    for (let i = 0; i < 3; i += 1) near((await rows.nth(i).boundingBox())!.height, 56);

    const first = rows.nth(0);
    await expect(first.locator('.ui-chip')).toHaveText('On');
    await expect(first.locator('.ui-chip')).toHaveClass(/ui-chip--done/);
    await expect(rows.nth(1).locator('.ui-chip')).toHaveText('Off');
    await expect(rows.nth(1).locator('.ui-chip')).toHaveClass(/ui-chip--stopped/);

    const buttons = first.locator('td.ui-list__actions button');
    await expect(buttons.nth(0)).toHaveAttribute('aria-label', 'Send test message My phone');
    await expect(buttons.nth(1)).toHaveAttribute('aria-label', 'Edit My phone');
    await expect(buttons.nth(2)).toHaveAttribute('aria-label', 'More actions for My phone');
  });

  test('Send test message sends the call, turns the icon off while it runs, and shows the result', async ({ page }) => {
    const state = await open(page);
    state.testDelayMs = 500;
    const test = page.getByRole('button', { name: 'Send test message My phone' });

    await test.click();

    await expect(test).toBeDisabled();
    await expect(page.getByText('The test message was sent.')).toBeVisible();
    await expect(test).toBeEnabled();
    expect(state.calls).toContain('POST /api/notifications/targets/t1/test');
  });

  test('Delete from the row menu asks first, Cancel sends no call, Delete removes the row', async ({ page }) => {
    const state = await open(page);

    await menuDelete(page, 'My phone');
    await expect(page.getByRole('dialog', { name: 'Delete target My phone?' })).toBeVisible();
    await page.getByRole('button', { name: 'Cancel' }).click();
    expect(state.calls.some((c) => c.startsWith('DELETE'))).toBe(false);

    await menuDelete(page, 'My phone');
    await page.getByRole('dialog', { name: 'Delete target My phone?' }).getByRole('button', { name: 'Delete' }).click();
    await expect(page.locator('[data-testid^="target-row-"]')).toHaveCount(2);
    expect(state.calls).toContain('DELETE /api/notifications/targets/t1');
  });

  test('Add target opens a 480 px side panel and the list stays visible', async ({ page }) => {
    await open(page);
    const addButton = page.getByRole('button', { name: 'Add target' });

    await addButton.click();

    const panel = page.getByTestId('target-editor');
    await expect(panel).toBeVisible();
    const box = (await panel.boundingBox())!;
    near(box.width, 480);
    near(box.x + box.width, 1280);
    await expect(page.locator('[data-testid^="target-row-"]').first()).toBeVisible();

    // SC-004: the Save button is fully inside the window, with no scroll.
    const save = (await panel.getByRole('button', { name: 'Save', exact: true }).boundingBox())!;
    expect(save.y + save.height).toBeLessThanOrEqual(800);

    await page.keyboard.press('Escape');
    await expect(panel).toBeHidden();
    await expect(addButton).toBeFocused();
  });

  test('while the panel is open, the list shows only Name, Enabled, and Actions, with no sideways scroll', async ({ page }) => {
    await open(page);
    await page.getByRole('button', { name: 'Add target' }).click();
    await expect(page.getByTestId('target-editor')).toBeVisible();

    const headers = page.locator('table.notification-target-list th');
    await expect(headers).toHaveText([/^Name$/i, /^Enabled$/i, /^Actions$/i]);
    const card = page.locator('.ui-list', { has: page.locator('table.notification-target-list') });
    const overflow = await card.evaluate((el) => el.scrollWidth - el.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
    // The actions of each row are visible and not under the panel.
    const panelLeft = (await page.getByTestId('target-editor').boundingBox())!.x;
    const more = (await page.getByRole('button', { name: 'More actions for My phone' }).boundingBox())!;
    expect(more.x + more.width).toBeLessThanOrEqual(panelLeft);

    await page.keyboard.press('Escape');
    await expect(headers).toHaveCount(6);

    // Rule D2: the row that is open in the panel has a mark.
    await page.getByRole('button', { name: 'Edit Team chat' }).click();
    await expect(page.getByTestId('target-row-t2')).toHaveClass(/ui-list__row--selected/);
    await expect(page.getByTestId('target-row-t1')).not.toHaveClass(/ui-list__row--selected/);
  });

  test('Save with empty fields shows both errors and sends no POST', async ({ page }) => {
    const state = await open(page);
    await page.getByRole('button', { name: 'Add target' }).click();

    await page.getByTestId('target-editor').getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.getByText('Enter a name.')).toBeVisible();
    await expect(page.getByText('Enter a chat ID.')).toBeVisible();
    expect(state.calls.some((c) => c.startsWith('POST /api/notifications/targets'))).toBe(false);
  });

  test('a click on a row opens the edit panel', async ({ page }) => {
    await open(page);

    await page.locator('[data-testid="target-row-t2"] td').first().click();

    const panel = page.getByTestId('target-editor');
    await expect(panel.getByRole('heading', { name: 'Edit target' })).toBeVisible();
    await expect(panel.getByLabel('Name')).toHaveValue('Team chat');
  });

  test('footer Delete closes the panel after the confirmation', async ({ page }) => {
    const state = await open(page);
    await page.locator('[data-testid="target-row-t2"] td').first().click();
    const panel = page.getByTestId('target-editor');

    await panel.getByRole('button', { name: 'Delete' }).click();
    await page.getByRole('dialog', { name: 'Delete target Team chat?' }).getByRole('button', { name: 'Delete' }).click();

    await expect(panel).toBeHidden();
    expect(state.calls).toContain('DELETE /api/notifications/targets/t2');
  });

  test('the row menu Delete of the open target also closes the panel', async ({ page }) => {
    await open(page);
    await page.locator('[data-testid="target-row-t2"] td').first().click();
    const panel = page.getByTestId('target-editor');
    await expect(panel).toBeVisible();

    await menuDelete(page, 'Team chat');
    await page.getByRole('dialog', { name: 'Delete target Team chat?' }).getByRole('button', { name: 'Delete' }).click();

    await expect(panel).toBeHidden();
  });

  test('the level list has a 240 px Level column, 56 px rows, and one PUT call for a change', async ({ page }) => {
    const state = await open(page);
    await expect(page.locator('[data-testid^="level-row-"]')).toHaveCount(3);

    near(await headerWidth(page, 'queue-level-table', 'Level'), 240);
    const rows = page.locator('[data-testid^="level-row-"]');
    for (let i = 0; i < 3; i += 1) near((await rows.nth(i).boundingBox())!.height, 56);

    await page.getByLabel('Notification level for Farm-1').selectOption('failure');

    await expect.poll(() => state.calls.filter((c) => c === 'PUT /api/queues/q1/notification-level').length).toBe(1);
  });
});
