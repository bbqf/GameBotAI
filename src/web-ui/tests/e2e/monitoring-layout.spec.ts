import { expect, test, Page } from '@playwright/test';
import { mockMonitoringApi } from './helpers/monitoringMock';

/**
 * Feature 204: the desktop Monitoring list and detail page. A jsdom test cannot measure layout, so this test runs in
 * a real browser. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

const headerWidth = async (page: Page, name: string) => {
  const box = await page.locator('table.session-list th', { hasText: new RegExp(`^${name}$`, 'i') }).boundingBox();
  return box!.width;
};

test.describe('Monitoring layout (desktop)', () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  test('list has fixed column widths, row heights, chips, and marks', async ({ page }) => {
    await mockMonitoringApi(page);
    await page.goto('/?area=monitoring');
    const rows = page.locator('[data-testid^="session-row-"]');
    await expect(rows).toHaveCount(3);

    near(await headerWidth(page, 'State'), 132);
    near(await headerWidth(page, 'Device'), 120);
    near(await headerWidth(page, 'Queue'), 220);
    near(await headerWidth(page, 'Sequence'), 260);
    await expect(page.getByRole('columnheader', { name: 'Actions' })).toHaveCount(0);
    for (let i = 0; i < 3; i += 1) near((await rows.nth(i).boundingBox())!.height, 56);

    const chips = page.getByTestId('session-state');
    await expect(chips.nth(0)).toHaveText('Live');
    await expect(chips.nth(0)).toHaveClass(/ui-chip--done/);
    await expect(chips.nth(1)).toHaveText('No answer');
    await expect(chips.nth(1)).toHaveClass(/ui-chip--failed/);

    const rowB = page.getByTestId('session-row-b');
    await expect(rowB.locator('td').nth(2)).toHaveText('—');
    await expect(rowB.locator('td').nth(3)).toHaveText('—');
    await expect(page.getByTestId('session-row-c').getByText('Not live')).toBeVisible();
  });

  test('opens the detail page with the picture and the card, and Back shows the list', async ({ page }) => {
    await mockMonitoringApi(page);
    await page.goto('/?area=monitoring');
    await page.getByTestId('session-row-a').click();
    const detail = page.getByTestId('monitoring-detail');
    await expect(detail).toBeVisible();
    await expect(page.locator('table.session-list')).toHaveCount(0);

    const side = await page.locator('.ui-detail-side').boundingBox();
    near(side!.width, 320);
    const picture = await page.getByTestId('monitoring-screenshot').boundingBox();
    expect(picture!.x + picture!.width).toBeLessThanOrEqual(side!.x + 1);
    await expect(detail.getByText('Farm gold')).toBeVisible();
    await expect(page.getByTestId('monitoring-sequence')).toHaveText('Daily rewards');

    await page.getByRole('button', { name: 'Back' }).click();
    await expect(page.locator('table.session-list')).toBeVisible();
  });

  test('restart dialog: cancel sends nothing, restart sends one call', async ({ page }) => {
    const state = await mockMonitoringApi(page);
    await page.goto('/?area=monitoring');
    await page.getByTestId('session-row-a').click();

    await page.getByRole('button', { name: 'Restart emulator' }).click();
    const dialog = page.getByRole('dialog', { name: 'Restart emulator Device A?' });
    await expect(dialog).toBeVisible();
    await expect(page.getByTestId('restart-queue-warning')).toContainText('Queue Farm gold uses this emulator');
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    expect(state.calls.filter((c) => c.startsWith('POST'))).toHaveLength(0);

    await page.getByRole('button', { name: 'Restart emulator' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Restart', exact: true }).click();
    await expect(page.getByTestId('restart-status')).toContainText('Restarting: stopping and starting the emulator');
    expect(state.calls.filter((c) => c.startsWith('POST'))).toHaveLength(1);
  });

  test('restart is off when it is not available', async ({ page }) => {
    await mockMonitoringApi(page);
    await page.goto('/?area=monitoring');
    await page.getByTestId('session-row-c').click();
    await expect(page.getByRole('button', { name: 'Restart emulator' })).toBeDisabled();
    await expect(page.getByTestId('restart-unavailable')).toContainText('Restart is not available for this session.');
    await expect(page.getByTestId('restart-unavailable')).toContainText('The emulator is not an LDPlayer instance.');
  });
});
