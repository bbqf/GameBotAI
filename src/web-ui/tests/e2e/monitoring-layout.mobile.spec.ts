import { expect, test, type Page } from '@playwright/test';
import { mockMonitoringApi } from './helpers/monitoringMock';

/**
 * Feature 204: the phone Monitoring list and detail page. The `mobile` project runs this file with a 360 x 640 touch
 * window. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

const noSideScroll = async (page: Page) => {
  for (const width of [320, 360, 390]) {
    await page.setViewportSize({ width, height: 640 });
    const fits = await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth);
    expect(fits).toBe(true);
  }
  await page.setViewportSize({ width: 360, height: 640 });
};

test.describe('Monitoring layout (phone)', () => {
  test('list rows are 64 px and the page has no sideways scroll', async ({ page }) => {
    await mockMonitoringApi(page);
    await page.goto('/?area=monitoring');
    const rows = page.locator('[data-testid^="session-row-"]');
    await expect(rows).toHaveCount(3);
    for (let i = 0; i < 3; i += 1) near((await rows.nth(i).boundingBox())!.height, 64);
    await noSideScroll(page);
  });

  test('detail page has a footer button of 44 px, no sideways scroll, and Back', async ({ page }) => {
    await mockMonitoringApi(page);
    await page.goto('/?area=monitoring');
    await page.getByTestId('session-row-a').getByRole('button', { name: 'Open Device A' }).tap();
    await expect(page.getByTestId('monitoring-detail')).toBeVisible();

    const button = page.getByRole('button', { name: 'Restart emulator' });
    await expect(button).toBeVisible();
    const box = (await button.boundingBox())!;
    expect(box.height).toBeGreaterThanOrEqual(43);
    const viewport = page.viewportSize()!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
    expect(box.y + box.height).toBeLessThanOrEqual(viewport.height);
    await noSideScroll(page);

    await page.getByRole('button', { name: 'Back' }).tap();
    await expect(page.locator('[data-testid^="session-row-"]')).toHaveCount(3);
  });
});
