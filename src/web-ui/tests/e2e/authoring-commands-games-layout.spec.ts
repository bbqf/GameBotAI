import { expect, test, Page } from '@playwright/test';
import { LONG_GAME_NAME, mockAuthoringApi } from './helpers/authoringMock';

/**
 * Feature 206: the Commands and Games pages on the shared UI components. A jsdom test cannot measure layout, so
 * this test runs in a real browser. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number, tolerance = 1) =>
  expect(Math.abs(actual - expected)).toBeLessThanOrEqual(tolerance);

const headerWidth = async (page: Page, table: string, name: string) => {
  const box = await page.locator(`table.${table} th`, { hasText: new RegExp(`^${name}$`, 'i') }).boundingBox();
  return box!.width;
};

const open = async (page: Page, tab: 'Games' | 'Commands', viewport = { width: 1280, height: 800 }) => {
  await page.setViewportSize(viewport);
  const state = await mockAuthoringApi(page);
  await page.goto('/');
  await page.getByRole('tab', { name: 'Authoring' }).click();
  await page.getByRole('tab', { name: tab }).click();
  await expect(page.locator(tab === 'Games' ? '[data-testid^="game-row-"]' : '[data-testid^="command-row-"]')).toHaveCount(3);
  return state;
};

const menuDelete = async (page: Page, name: string) => {
  const more = page.getByRole('button', { name: `More actions for ${name}` });
  // The menu closes on a scroll, so the row must be in view before the click.
  await more.scrollIntoViewIfNeeded();
  await more.click();
  await page.getByRole('menuitem', { name: /Delete/ }).click();
};

const noSidewaysScroll = async (page: Page) => {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
};

test.describe('Games layout (desktop)', () => {
  test('C-01, C-02, C-04: header, filter, columns, rows, and row actions', async ({ page }) => {
    await open(page, 'Games');
    await expect(page.getByRole('heading', { name: 'Games' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Create game' })).toHaveCount(1);
    await expect(page.locator('th').getByLabel('Filter by name')).toHaveCount(0);
    const filter = await page.getByLabel('Filter by name').boundingBox();
    const table = await page.locator('table.games-list').boundingBox();
    expect(filter!.y + filter!.height).toBeLessThanOrEqual(table!.y);

    near(await headerWidth(page, 'games-list', 'Package name'), 320);
    const rows = page.locator('[data-testid^="game-row-"]');
    for (let i = 0; i < 3; i += 1) near((await rows.nth(i).boundingBox())!.height, 56);
    await expect(page.getByTestId('game-row-g2')).toContainText('—');

    await expect(page.getByRole('button', { name: 'Edit Alpha Game' })).toBeVisible();
    await page.getByRole('button', { name: 'More actions for Alpha Game' }).click();
    const items = page.getByRole('menuitem');
    await expect(items.last()).toHaveText(/Delete/);
  });

  test('C-05, C-06, C-07, C-10: row click opens the form page with a 720 px card and a footer bar', async ({ page }) => {
    await open(page, 'Games');
    await page.getByTestId('game-row-g1').click();
    await expect(page.getByRole('button', { name: 'Back' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Alpha Game' })).toBeVisible();

    const form = page.locator('.ui-form');
    const box = (await form.boundingBox())!;
    near(box.width, 720);
    const parent = (await form.locator('..').boundingBox())!;
    near(box.x - parent.x, parent.x + parent.width - (box.x + box.width), 2);

    await expect(page.locator('.ui-footer-bar')).toBeInViewport();
    const buttons = page.locator('.ui-footer-bar button');
    await expect(buttons).toHaveText(['Delete', 'Cancel', 'Save']);

    await page.getByRole('button', { name: 'Back' }).click();
    await expect(page.getByRole('heading', { name: 'Games' })).toBeVisible();
  });

  test('C-07, C-12: create mode has the title "New game" and no Delete', async ({ page }) => {
    await open(page, 'Games');
    await page.getByRole('button', { name: 'Create game' }).click();
    await expect(page.getByRole('heading', { name: 'New game' })).toBeVisible();
    await expect(page.locator('.ui-footer-bar button')).toHaveText(['Cancel', 'Save']);
    await expect(page.locator('.ui-footer-bar')).toBeInViewport();
  });

  test('C-08, C-11: delete from the row menu shows the dialog and the message', async ({ page }) => {
    await open(page, 'Games');
    await menuDelete(page, 'Beta Game');
    const dialog = page.getByRole('dialog', { name: 'Delete game Beta Game?' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText('You cannot undo this.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText('Game deleted.')).toBeVisible();
    await expect(page.locator('[data-testid^="game-row-"]')).toHaveCount(2);
  });

  test('C-09: a 409 answer keeps the dialog open with the references and turns Delete off', async ({ page }) => {
    const state = await open(page, 'Games');
    state.conflictIds.add('g1');
    await menuDelete(page, 'Alpha Game');
    const dialog = page.getByRole('dialog', { name: 'Delete game Alpha Game?' });
    await dialog.getByRole('button', { name: 'Delete' }).click();
    await expect(dialog.getByText('Referenced by:')).toBeVisible();
    await expect(dialog.getByText(/Daily farm/)).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Delete' })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toHaveCount(0);
  });

  test('C-14: a game with a name of 200 characters keeps the row height and a one-line title', async ({ page }) => {
    await open(page, 'Games');
    near((await page.getByTestId('game-row-g3').boundingBox())!.height, 56);
    await page.getByTestId('game-row-g3').click();
    const title = page.getByRole('heading', { name: LONG_GAME_NAME });
    await expect(title).toBeVisible();
    const lineHeight = await title.evaluate((el) => parseFloat(getComputedStyle(el).lineHeight) || 30);
    expect((await title.boundingBox())!.height).toBeLessThan(lineHeight * 2);
  });

  test('C-13: no sideways scroll at 1024 x 768', async ({ page }) => {
    await open(page, 'Games', { width: 1024, height: 768 });
    await noSidewaysScroll(page);
    await page.getByTestId('game-row-g3').click();
    await expect(page.getByRole('button', { name: 'Back' })).toBeVisible();
    await noSidewaysScroll(page);
  });
});

test.describe('Commands layout (desktop)', () => {
  test('C-03, C-04: columns Name and Steps, no Game column, row actions', async ({ page }) => {
    await open(page, 'Commands');
    await expect(page.getByRole('heading', { name: 'Commands' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Create command' })).toHaveCount(1);
    near(await headerWidth(page, 'commands-list', 'Steps'), 96);
    await expect(page.locator('table.commands-list th', { hasText: /^Game$/ })).toHaveCount(0);
    await expect(page.getByText('All games')).toHaveCount(0);
    const rows = page.locator('[data-testid^="command-row-"]');
    for (let i = 0; i < 3; i += 1) near((await rows.nth(i).boundingBox())!.height, 56);

    await expect(page.getByRole('button', { name: 'Edit Open shop' })).toBeVisible();
    await page.getByRole('button', { name: 'More actions for Open shop' }).click();
    await expect(page.getByRole('menuitem').last()).toHaveText(/Delete/);
  });

  test('C-05, C-06, C-07, C-10: row click opens the form page with a 720 px card and a footer bar', async ({ page }) => {
    await open(page, 'Commands');
    await page.getByTestId('command-row-c1').click();
    await expect(page.getByRole('button', { name: 'Back' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Open shop' })).toBeVisible();

    const form = page.locator('.ui-form');
    const box = (await form.boundingBox())!;
    near(box.width, 720);
    const parent = (await form.locator('..').boundingBox())!;
    near(box.x - parent.x, parent.x + parent.width - (box.x + box.width), 2);

    await expect(page.locator('.ui-footer-bar')).toBeInViewport();
    await expect(page.locator('.ui-footer-bar button')).toHaveText(['Delete', 'Cancel', 'Save']);

    await page.getByRole('button', { name: 'Back' }).click();
    await expect(page.getByRole('heading', { name: 'Commands' })).toBeVisible();
  });

  test('C-12: create mode has the title "New command" and no Delete in the footer', async ({ page }) => {
    await open(page, 'Commands');
    await page.getByRole('button', { name: 'Create command' }).click();
    await expect(page.getByRole('heading', { name: 'New command' })).toBeVisible();
    await expect(page.locator('.ui-footer-bar button')).toHaveText(['Cancel', 'Save']);
  });

  test('C-08, C-09, C-11: delete shows the dialog, the 409 references, and the message', async ({ page }) => {
    const state = await open(page, 'Commands');
    state.conflictIds.add('c1');
    await menuDelete(page, 'Open shop');
    const dialog = page.getByRole('dialog', { name: 'Delete command Open shop?' });
    await expect(dialog.getByText('You cannot undo this.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Delete' }).click();
    await expect(dialog.getByText('Referenced by:')).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Delete' })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Cancel' }).click();

    await menuDelete(page, 'Close shop');
    await page.getByRole('dialog', { name: 'Delete command Close shop?' }).getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText('Command deleted.')).toBeVisible();
    await expect(page.locator('[data-testid^="command-row-"]')).toHaveCount(2);
  });

  test('C-13: no sideways scroll at 1024 x 768', async ({ page }) => {
    await open(page, 'Commands', { width: 1024, height: 768 });
    await noSidewaysScroll(page);
    await page.getByTestId('command-row-c1').click();
    await expect(page.getByRole('button', { name: 'Back' })).toBeVisible();
    await noSidewaysScroll(page);
  });
});
