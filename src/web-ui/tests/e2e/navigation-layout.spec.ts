import { expect, test, Page } from '@playwright/test';
import { mockNotificationsApi } from './helpers/notificationsMock';

/**
 * Feature 205: the desktop navigation (the app bar and the sub-tabs). A jsdom test cannot measure layout, so this
 * test runs in a real browser. All API routes are mocked. A tolerance of 1 px applies. See the contract
 * `specs/205-navigation-shared-ui/contracts/navigation-layout.md` (D-01 to D-14).
 */
const near = (actual: number, expected: number) => expect(Math.abs(actual - expected)).toBeLessThanOrEqual(1);

const json = { 'content-type': 'application/json' };

const open = async (page: Page, address = '/?area=queues') => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await mockNotificationsApi(page);
  // The Monitoring page reads its own list.
  await page.route('**/api/sessions/monitoring', (route) =>
    route.fulfill({ status: 200, headers: json, body: JSON.stringify({ sessions: [] }) }));
  await page.goto(address);
};

const topTab = (page: Page, name: string) =>
  page.getByRole('navigation', { name: 'Primary Navigation' }).getByRole('tab', { name, exact: true });

const titles = (page: Page) => page.locator('.ui-page-header__title');

test.describe('Navigation layout (desktop)', () => {
  test('D-01 to D-04, D-09, D-13: the app bar has the brand, 5 tabs with icons, and the connection state', async ({ page }) => {
    await open(page);

    const bar = page.locator('header.ui-appbar');
    near((await bar.boundingBox())!.height, 56);
    await expect(bar.locator('.ui-appbar__brand')).toHaveText('GameBot');
    const tabs = page.getByRole('navigation', { name: 'Primary Navigation' }).getByRole('tab');
    await expect(tabs).toHaveText(['Queues', 'Monitor', 'Alerts', 'Authoring', 'Settings']);
    for (const tab of await tabs.all()) await expect(tab.locator('svg')).toHaveCount(1);
    await expect(topTab(page, 'Queues')).toHaveAttribute('aria-selected', 'true');
    await expect(bar.getByRole('status')).toHaveText('Online');
    await expect(page.getByText('GameBot Web UI')).toHaveCount(0);
    await expect(page).toHaveTitle('GameBot');
  });

  test('D-04: the connection state shows Connection lost when the health call fails', async ({ page }) => {
    await open(page);
    await page.route('**/health', (route) => route.fulfill({ status: 503, body: '' }));
    await page.reload();

    await expect(page.locator('header.ui-appbar').getByRole('status')).toHaveText('Connection lost');
  });

  test('D-05, D-11, D-14: Settings opens Execution logs with 5 sub-tabs and one title', async ({ page }) => {
    await open(page);

    await topTab(page, 'Settings').click();

    const sub = page.getByRole('navigation', { name: 'Settings Navigation' });
    near((await sub.boundingBox())!.height, 44);
    await expect(sub.getByRole('tab')).toHaveText(['Execution logs', 'Execution', 'Configuration', 'Gamepacks', 'Update']);
    await expect(sub.getByRole('tab', { name: 'Execution logs' })).toHaveAttribute('aria-selected', 'true');
    await expect(titles(page)).toHaveText(['Execution logs']);
    await expect(topTab(page, 'Settings')).toHaveAttribute('aria-selected', 'true');

    for (const [name, title] of [['Execution', 'Execution'], ['Configuration', 'Configuration'], ['Gamepacks', 'Gamepacks'], ['Update', 'Update']]) {
      await sub.getByRole('tab', { name, exact: true }).click();
      await expect(sub.getByRole('tab', { name, exact: true })).toHaveAttribute('aria-selected', 'true');
      await expect(page.getByRole('heading', { name: title, exact: true })).toHaveCount(1);
    }
  });

  test('D-06: Authoring shows its sub-tabs and no Authoring heading', async ({ page }) => {
    await open(page, '/');

    const sub = page.getByRole('navigation', { name: 'Authoring Navigation' });
    await expect(sub.getByRole('tab')).toHaveText(['Commands', 'Games', 'Sequences', 'Images', 'Backup & Restore']);
    await expect(page.getByRole('heading', { name: 'Authoring', exact: true })).toHaveCount(0);
  });

  test('D-07: old addresses open the page with Settings and the sub-tab selected', async ({ page }) => {
    for (const area of ['configuration', 'update', 'gamepacks', 'execution', 'execution-logs']) {
      await open(page, `/?area=${area}`);
      await expect(topTab(page, 'Settings')).toHaveAttribute('aria-selected', 'true');
      await expect(page.getByRole('navigation', { name: 'Settings Navigation' }).locator('[aria-selected="true"]')).toHaveCount(1);
    }
    await open(page, '/?area=more');
    await expect(page.getByRole('navigation', { name: 'Settings Navigation' }).getByRole('tab', { name: 'Execution logs' }))
      .toHaveAttribute('aria-selected', 'true');
  });

  test('D-08, D-11: Monitor and Alerts have the new titles', async ({ page }) => {
    await open(page, '/?area=monitoring');
    await expect(topTab(page, 'Monitor')).toHaveAttribute('aria-selected', 'true');
    await expect(titles(page)).toHaveText(['Monitor']);

    await open(page, '/?area=notifications');
    await expect(topTab(page, 'Alerts')).toHaveAttribute('aria-selected', 'true');
    await expect(titles(page)).toHaveText(['Alerts']);
  });

  test('D-10: the address names no area: Authoring > Commands shows', async ({ page }) => {
    await open(page, '/');

    await expect(topTab(page, 'Authoring')).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('navigation', { name: 'Authoring Navigation' }).getByRole('tab', { name: 'Commands' }))
      .toHaveAttribute('aria-selected', 'true');
  });

  test('D-12: at 1024 x 768 the app bar stays 56 px high and the items sit on one line', async ({ page }) => {
    await open(page);
    await page.setViewportSize({ width: 1024, height: 768 });

    near((await page.locator('header.ui-appbar').boundingBox())!.height, 56);
    const tops = [
      ...(await page.getByRole('navigation', { name: 'Primary Navigation' }).getByRole('tab').all()),
      page.locator('header.ui-appbar').getByRole('status')
    ];
    // The items have different heights, so the check compares the vertical centre.
    const centre = async (item: (typeof tops)[number]) => {
      const box = (await item.boundingBox())!;
      return box.y + box.height / 2;
    };
    const first = await centre(tops[0]);
    for (const item of tops) near(await centre(item), first);
  });

  test('D-14: each area opens in at most 2 clicks from Queues', async ({ page }) => {
    await open(page);

    for (const name of ['Monitor', 'Alerts', 'Authoring']) {
      await topTab(page, name).click();
      await expect(topTab(page, name)).toHaveAttribute('aria-selected', 'true');
    }
    for (const sub of ['Execution logs', 'Execution', 'Configuration', 'Gamepacks', 'Update']) {
      await topTab(page, 'Queues').click();
      await topTab(page, 'Settings').click();
      await page.getByRole('navigation', { name: 'Settings Navigation' }).getByRole('tab', { name: sub, exact: true }).click();
      await expect(page.getByRole('navigation', { name: 'Settings Navigation' }).getByRole('tab', { name: sub, exact: true }))
        .toHaveAttribute('aria-selected', 'true');
    }
  });
});
