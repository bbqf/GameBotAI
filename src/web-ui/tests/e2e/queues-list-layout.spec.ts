import { expect, test, Page } from '@playwright/test';
import { LONG_NAME, mockQueuesApi } from './helpers/queuesMock';

/**
 * Feature 202: the desktop Queues list has fixed column widths and row heights. A jsdom test cannot measure layout,
 * so this test runs in a real browser. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

const headerWidth = async (page: Page, name: string) => {
  const box = await page.locator('table.queues-table th', { hasText: new RegExp(`^${name}$`, 'i') }).boundingBox();
  return box!.width;
};

const rowOf = (page: Page, name: string) => page.locator('tr.queues-row', { has: page.locator(`td button.ui-list__open`, { hasText: name }) });

test.describe('Queues list layout (desktop)', () => {
  test('has fixed column widths and row heights', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockQueuesApi(page);
    await page.goto('/?area=queues');
    await expect(page.locator('tr.queues-row')).toHaveCount(5);

    near(await headerWidth(page, 'Cycle'), 96);
    near(await headerWidth(page, 'Status'), 132);
    near(await headerWidth(page, 'Activity'), 320);
    near(await headerWidth(page, 'Actions'), 184);

    const headerBox = await page.locator('table.queues-table thead tr').boundingBox();
    near(headerBox!.height, 40);
    const rows = page.locator('tr.queues-row');
    for (let i = 0; i < 5; i += 1) {
      near((await rows.nth(i).boundingBox())!.height, 56);
    }

    const longCell = page.locator('tr.queues-row td').first();
    const longOpen = page.locator('tr.queues-row').nth(3).locator('td').first();
    expect(await longOpen.evaluate((el) => el.scrollWidth > el.clientWidth || (el.firstElementChild as HTMLElement).scrollWidth > (el.firstElementChild as HTMLElement).clientWidth)).toBe(true);
    await expect(page.locator('tr.queues-row').nth(3).locator('td').first()).toHaveAttribute('title', LONG_NAME);
    await expect(longCell).toHaveAttribute('title', 'PNS Daily');

    const chips = page.getByTestId('queue-status');
    await expect(chips.nth(0)).toHaveText('Stopped');
    await expect(chips.nth(0)).toHaveClass(/ui-chip--stopped/);
    await expect(chips.nth(1)).toHaveText('Running');
    await expect(chips.nth(1)).toHaveClass(/ui-chip--running/);
    await expect(chips.nth(2)).toHaveText('Paused');
    await expect(chips.nth(2)).toHaveClass(/ui-chip--paused/);
  });

  test('does not change the widths and heights when a queue starts', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    const state = await mockQueuesApi(page);
    await page.goto('/?area=queues');
    await expect(page.locator('tr.queues-row')).toHaveCount(5);

    const measure = async () => ({
      cycle: await headerWidth(page, 'Cycle'),
      status: await headerWidth(page, 'Status'),
      activity: await headerWidth(page, 'Activity'),
      actions: await headerWidth(page, 'Actions'),
      row: (await rowOf(page, 'PNS Daily').boundingBox())!.height,
    });
    const before = await measure();

    await page.getByRole('button', { name: 'Start PNS Daily' }).click();
    await expect(rowOf(page, 'PNS Daily').getByTestId('queue-status')).toHaveText('Running');
    expect(state.calls).toContain('POST /api/queues/q1/start');
    const after = await measure();

    expect(after).toEqual(before);
  });

  test('keeps each row at 56 px at a 1024 px window', async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 800 });
    await mockQueuesApi(page);
    await page.goto('/?area=queues');
    await expect(page.locator('tr.queues-row')).toHaveCount(5);
    const rows = page.locator('tr.queues-row');
    for (let i = 0; i < 5; i += 1) {
      near((await rows.nth(i).boundingBox())!.height, 56);
    }
  });

  test('has the row actions in fixed places', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockQueuesApi(page);
    await page.goto('/?area=queues');
    await expect(page.locator('tr.queues-row')).toHaveCount(5);

    const names = async (name: string) =>
      rowOf(page, name).locator('td.ui-list__actions button').evaluateAll((buttons) => buttons.map((b) => b.getAttribute('aria-label')));

    expect(await names('PNS Daily')).toEqual(['Start PNS Daily', 'Stop PNS Daily', 'Edit PNS Daily', 'More actions for PNS Daily']);
    await expect(page.getByRole('button', { name: 'Stop PNS Daily' })).toBeDisabled();
    expect(await names('Running Queue')).toEqual(['Pause Running Queue', 'Stop Running Queue', 'Edit Running Queue', 'More actions for Running Queue']);
    expect(await names('Paused Queue')).toEqual(['Resume Paused Queue', 'Stop Paused Queue', 'Edit Paused Queue', 'More actions for Paused Queue']);

    const startBox = await page.getByRole('button', { name: 'Start PNS Daily' }).boundingBox();
    const pauseBox = await page.getByRole('button', { name: 'Pause Running Queue' }).boundingBox();
    near(startBox!.x, pauseBox!.x);
  });

  test('deletes a queue through the menu and the dialog', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    const state = await mockQueuesApi(page);
    await page.goto('/?area=queues');
    await expect(page.locator('tr.queues-row')).toHaveCount(5);

    await page.getByRole('button', { name: 'More actions for PNS Daily' }).click();
    await expect(page.getByRole('menuitem')).toHaveText(['Duplicate', 'Delete']);
    await page.getByRole('menuitem', { name: 'Delete' }).click();

    const dialog = page.getByRole('dialog', { name: 'Delete queue PNS Daily?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toBeHidden();
    expect(state.calls.some((c) => c.startsWith('DELETE'))).toBe(false);

    await page.getByRole('button', { name: 'More actions for PNS Daily' }).click();
    await page.getByRole('menuitem', { name: 'Delete' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText('Queue deleted successfully.')).toBeVisible();
    expect(state.calls).toContain('DELETE /api/queues/q1');
    await expect(page.locator('tr.queues-row')).toHaveCount(4);
  });

  test('opens the edit form with a click on the name', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockQueuesApi(page);
    await page.goto('/?area=queues');
    await page.getByRole('button', { name: 'PNS Daily', exact: true }).click();
    await expect(page.getByText('Edit Queue')).toBeVisible();
  });
});