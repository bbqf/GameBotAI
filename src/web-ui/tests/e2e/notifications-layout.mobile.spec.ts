import { expect, test, Page } from '@playwright/test';
import { mockNotificationsApi } from './helpers/notificationsMock';

/**
 * Feature 203: the Notifications area on a phone (360 x 640). The list rows are 64 px, the touch targets are at
 * least 44 px, and the form is a full page. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

const open = async (page: Page) => {
  await mockNotificationsApi(page);
  await page.goto('/?area=notifications');
  await expect(page.locator('[data-testid^="target-row-"]')).toHaveCount(3);
  await expect(page.locator('[data-testid^="level-row-"]')).toHaveCount(3);
};

const noHorizontalScroll = async (page: Page) =>
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);

test.describe('Notifications layout (phone)', () => {
  test('rows have the right sizes and the page does not scroll sideways', async ({ page }) => {
    await open(page);

    for (const row of await page.locator('[data-testid^="target-row-"], [data-testid^="level-row-"]').all()) {
      near((await row.boundingBox())!.height, 64);
      for (const button of await row.locator('button.ui-icon-button').all()) {
        const box = (await button.boundingBox())!;
        expect(box.width).toBeGreaterThanOrEqual(43.5);
        expect(box.height).toBeGreaterThanOrEqual(43.5);
      }
      for (const select of await row.locator('select').all()) {
        expect((await select.boundingBox())!.height).toBeGreaterThanOrEqual(39.5);
      }
    }

    for (const width of [320, 360, 390]) {
      await page.setViewportSize({ width, height: 640 });
      await noHorizontalScroll(page);
    }
  });

  test('Add target opens a full page with Back, and Back shows the list again', async ({ page }) => {
    await open(page);

    await page.getByRole('button', { name: 'Add target' }).click();

    const panel = page.getByTestId('target-editor');
    await expect(panel.getByRole('heading', { name: 'Add target' })).toBeVisible();
    await expect(panel.getByRole('button', { name: 'Back' })).toBeVisible();
    await expect(page.locator('[data-testid^="target-row-"]')).toHaveCount(0);
    for (const width of [320, 360, 390]) {
      await page.setViewportSize({ width, height: 640 });
      await noHorizontalScroll(page);
    }

    await panel.getByRole('button', { name: 'Back' }).click();

    await expect(page.locator('[data-testid^="target-row-"]')).toHaveCount(3);
  });
});
