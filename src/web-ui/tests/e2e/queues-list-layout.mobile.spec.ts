import { expect, test } from '@playwright/test';
import { mockQueuesApi } from './helpers/queuesMock';

/**
 * Feature 202: the phone Queues list uses the phone mode of the shared list. The `mobile` project runs this file with a
 * 360 x 640 touch window. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

test.describe('Queues list layout (phone)', () => {
  test('has rows of 64 px, touch buttons, and a menu', async ({ page }) => {
    const state = await mockQueuesApi(page);
    await page.goto('/?area=queues');
    const cards = page.locator('[data-testid^="queue-card-"]');
    await expect(cards).toHaveCount(5);

    for (let i = 0; i < 5; i += 1) {
      near((await cards.nth(i).boundingBox())!.height, 64);
    }

    await expect(page.getByRole('button', { name: 'Start PNS Daily' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Stop Running Queue' })).toBeVisible();

    const buttons = page.locator('[data-testid^="queue-card-"] .ui-icon-button');
    const count = await buttons.count();
    expect(count).toBe(10);
    for (let i = 0; i < count; i += 1) {
      const box = (await buttons.nth(i).boundingBox())!;
      expect(box.width).toBeGreaterThanOrEqual(44 - 1);
      expect(box.height).toBeGreaterThanOrEqual(44 - 1);
    }

    await page.getByRole('button', { name: 'More actions for Running Queue' }).click();
    await expect(page.getByRole('menuitem')).toHaveText(['Open', 'Pause']);
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'More actions for PNS Daily' }).click();
    await expect(page.getByRole('menuitem')).toHaveText(['Open']);
    await page.keyboard.press('Escape');

    await page.getByRole('button', { name: 'Start PNS Daily' }).tap();
    await expect(page.getByRole('button', { name: 'Stop PNS Daily' })).toBeVisible();
    expect(state.calls).toContain('POST /api/queues/q1/start');
  });

  test('opens the queue detail with a tap on the row title', async ({ page }) => {
    await mockQueuesApi(page);
    await page.goto('/?area=queues');
    await page.locator('[data-testid="queue-card-q1"] .ui-list__phone-title').tap();
    await expect(page.getByTestId('phone-queue-detail')).toBeVisible();
  });

  test('shows the Not live mark and keeps the row at 64 px', async ({ page }) => {
    await mockQueuesApi(page);
    await page.goto('/?area=queues');
    const card = page.getByTestId('queue-card-q5');
    await expect(card.getByText('Not live')).toBeVisible();
    near((await card.boundingBox())!.height, 64);
  });

  for (const width of [320, 360, 390]) {
    test(`has no horizontal scroll at ${width} px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 640 });
      await mockQueuesApi(page);
      await page.goto('/?area=queues');
      await expect(page.locator('[data-testid^="queue-card-"]')).toHaveCount(5);
      const sizes = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
      expect(sizes.scroll).toBeLessThanOrEqual(sizes.client);
    });
  }
});