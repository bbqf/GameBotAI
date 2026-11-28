import { expect, test, Page } from '@playwright/test';
import { mockNotificationsApi } from './helpers/notificationsMock';

/**
 * Feature 205: the phone navigation (the bottom bar and the More page) at 360 x 640. A jsdom test cannot measure
 * layout, so this test runs in a real browser. All API routes are mocked. A tolerance of 1 px applies. See the
 * contract `specs/205-navigation-shared-ui/contracts/navigation-layout.md` (P-01 to P-09).
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

const json = { 'content-type': 'application/json' };

const open = async (page: Page, address = '/?area=queues') => {
  await mockNotificationsApi(page);
  await page.route('**/api/sessions/monitoring', (route) =>
    route.fulfill({ status: 200, headers: json, body: JSON.stringify({ sessions: [] }) }));
  await page.goto(address);
};

const bar = (page: Page) => page.getByRole('navigation', { name: 'Primary Navigation' });

const noHorizontalScroll = async (page: Page) =>
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);

test.describe('Navigation layout (phone)', () => {
  test('P-01: the bottom bar has 4 icon buttons and is 64 px high', async ({ page }) => {
    await open(page);

    const buttons = bar(page).getByRole('button');
    await expect(buttons).toHaveText(['Queues', 'Monitor', 'Alerts', 'More']);
    for (const button of await buttons.all()) {
      await expect(button.locator('svg')).toHaveCount(1);
      expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(43.5);
    }
    const box = (await bar(page).boundingBox())!;
    expect(box.height).toBeGreaterThanOrEqual(63);
    expect(box.height).toBeLessThanOrEqual(65);
  });

  test('P-02, P-03: More opens the More page with 3 rows and 2 dimmed rows', async ({ page }) => {
    await open(page);

    await bar(page).getByRole('button', { name: 'More' }).click();

    await expect(bar(page).getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'page');
    expect(page.url()).toContain('area=more');
    await expect(page.locator('.phone-header__title')).toHaveText('More');
    await expect(page.getByRole('button', { name: 'Back' })).toHaveCount(0);
    const rows = page.locator('button.phone-more__row');
    await expect(rows).toHaveText(['Configuration', 'Gamepacks', 'Update']);
    for (const row of await rows.all()) near((await row.boundingBox())!.height, 56);
    await expect(page.getByText('Desktop only', { exact: true })).toBeVisible();
    const dimmed = page.locator('.phone-more__row[aria-disabled="true"]');
    await expect(dimmed).toHaveCount(2);
    await expect(dimmed.nth(0)).toContainText('Execution logs');
    await expect(dimmed.nth(1)).toContainText('Authoring');
    for (const row of await dimmed.all()) await expect(row).toContainText('Use a computer');
  });

  test('P-04: a tap on a dimmed row does not change the page', async ({ page }) => {
    await open(page, '/?area=more');

    await page.locator('.phone-more__row[aria-disabled="true"]').first().click();

    expect(page.url()).toContain('area=more');
    await expect(page.getByText('Desktop only', { exact: true })).toBeVisible();
  });

  test('P-05: Configuration opens with a Back arrow that returns to More', async ({ page }) => {
    await open(page, '/?area=more');

    await page.getByRole('button', { name: 'Configuration', exact: true }).click();

    await expect(page.locator('.phone-header__title')).toHaveText('Configuration');
    await expect(bar(page).getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'page');
    await page.getByRole('button', { name: 'Back', exact: true }).click();
    await expect(page.getByText('Desktop only', { exact: true })).toBeVisible();
  });

  test('P-06: no sideways scroll at 320, 360, and 390 px on Queues and on the More page', async ({ page }) => {
    await open(page);
    for (const width of [320, 360, 390]) {
      await page.setViewportSize({ width, height: 640 });
      await noHorizontalScroll(page);
    }
    await bar(page).getByRole('button', { name: 'More' }).click();
    await expect(page.getByText('Desktop only', { exact: true })).toBeVisible();
    for (const width of [320, 360, 390]) {
      await page.setViewportSize({ width, height: 640 });
      await noHorizontalScroll(page);
    }
  });

  test('P-07: ?area=authoring shows the notice and Back to More opens More', async ({ page }) => {
    await open(page, '/?area=authoring');

    await expect(page.getByText('Use a larger screen for this area.')).toBeVisible();
    await page.getByRole('button', { name: 'Back to More' }).click();

    await expect(page.getByText('Desktop only', { exact: true })).toBeVisible();
  });

  test('P-08: the browser back control after Queues, More, Configuration shows More, then Queues', async ({ page }) => {
    await open(page);
    await bar(page).getByRole('button', { name: 'More' }).click();
    await page.getByRole('button', { name: 'Configuration', exact: true }).click();
    await expect(page.locator('.phone-header__title')).toHaveText('Configuration');

    await page.goBack();
    await expect(page.getByText('Desktop only', { exact: true })).toBeVisible();
    await page.goBack();
    await expect(page.getByTestId('phone-queues')).toBeVisible();
  });

  test('P-09: ?area=execution shows Queues', async ({ page }) => {
    await open(page, '/?area=execution');

    await expect(page.getByTestId('phone-queues')).toBeVisible();
  });

  test('SC-002: each phone area opens in at most 2 taps from Queues', async ({ page }) => {
    await open(page);
    for (const name of ['Monitor', 'Alerts']) {
      await bar(page).getByRole('button', { name }).click();
      await expect(bar(page).getByRole('button', { name })).toHaveAttribute('aria-current', 'page');
    }
    for (const name of ['Configuration', 'Gamepacks', 'Update']) {
      await bar(page).getByRole('button', { name: 'Queues' }).click();
      await bar(page).getByRole('button', { name: 'More' }).click();
      await page.getByRole('button', { name, exact: true }).click();
      await expect(page.locator('.phone-header__title')).toHaveText(name);
    }
  });
});
