import { expect, test, Page } from '@playwright/test';
import { LONG_SEQUENCE_NAME, mockAuthoringApi } from './helpers/authoringMock';

/**
 * Feature 207: the Sequences page on the shared UI components. A jsdom test cannot measure layout, so this test
 * runs in a real browser. All API routes are mocked. A tolerance of 1 px applies.
 */
const near = (actual: number, expected: number, tolerance = 1) =>
  expect(Math.abs(actual - expected)).toBeLessThanOrEqual(tolerance);

const headerWidth = async (page: Page, name: string) => {
  const box = await page.locator('table.sequences-list th', { hasText: new RegExp(`^${name}$`, 'i') }).boundingBox();
  return box!.width;
};

const open = async (page: Page, viewport = { width: 1280, height: 800 }) => {
  await page.setViewportSize(viewport);
  const state = await mockAuthoringApi(page);
  await page.goto('/');
  await page.getByRole('tab', { name: 'Authoring' }).click();
  await page.getByRole('tab', { name: 'Sequences' }).click();
  await expect(page.locator('[data-testid^="sequence-row-"]')).toHaveCount(3);
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

test.describe('Sequences layout (desktop)', () => {
  test('S-01, S-02, S-03, S-04, S-12: header, filter, columns, rows, options, and row actions', async ({ page }) => {
    await open(page);
    await expect(page.getByRole('heading', { name: 'Sequences' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Create sequence' })).toHaveCount(1);
    await expect(page.locator('th').getByLabel('Filter by name')).toHaveCount(0);
    const filter = await page.getByLabel('Filter by name').boundingBox();
    const table = await page.locator('table.sequences-list').boundingBox();
    expect(filter!.y + filter!.height).toBeLessThanOrEqual(table!.y);

    near(await headerWidth(page, 'Options'), 360);
    near(await headerWidth(page, 'Steps'), 96);
    const rows = page.locator('[data-testid^="sequence-row-"]');
    for (let i = 0; i < 3; i += 1) near((await rows.nth(i).boundingBox())!.height, 56);

    await expect(page.getByTestId('sequence-row-s1')).toContainText('No success notifications, Ignores smart scheduling');
    await expect(page.getByTestId('sequence-row-s3')).toContainText('—');
    await expect(page.getByText('No sequences yet')).toHaveCount(0);

    await expect(page.getByRole('button', { name: 'Edit Daily farm' })).toBeVisible();
    await page.getByRole('button', { name: 'More actions for Daily farm' }).click();
    await expect(page.getByRole('menuitem').last()).toHaveText(/Delete/);
  });

  test('S-05, S-06, S-07, S-10: row click opens the form page with an 880 px card and a footer bar', async ({ page }) => {
    await open(page);
    await page.getByTestId('sequence-row-s1').click();
    await expect(page.getByRole('button', { name: 'Back' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Daily farm' })).toBeVisible();
    const header = page.locator('header.ui-page-header');
    await expect(header.getByRole('button', { name: 'Step through' })).toBeVisible();

    const form = page.locator('.ui-form--wide');
    const box = (await form.boundingBox())!;
    near(box.width, 880);
    const parent = (await form.locator('..').boundingBox())!;
    near(box.x - parent.x, parent.x + parent.width - (box.x + box.width), 2);

    await expect(page.locator('.ui-footer-bar')).toBeInViewport();
    await expect(page.locator('.ui-footer-bar button')).toHaveText(['Delete', 'Cancel', 'Save']);

    await page.getByRole('button', { name: 'Back' }).click();
    await expect(page.getByRole('heading', { name: 'Sequences' })).toBeVisible();
  });

  test('S-07: create mode has the title "New sequence" and no Delete', async ({ page }) => {
    await open(page);
    await page.getByRole('button', { name: 'Create sequence' }).click();
    await expect(page.getByRole('heading', { name: 'New sequence' })).toBeVisible();
    await expect(page.locator('.ui-footer-bar button')).toHaveText(['Cancel', 'Save']);
    await expect(page.locator('.ui-footer-bar')).toBeInViewport();
  });

  test('S-08, S-09: delete shows the dialog, the 409 references, and the message', async ({ page }) => {
    const state = await open(page);
    state.conflictIds.add('s1');
    await menuDelete(page, 'Daily farm');
    const dialog = page.getByRole('dialog', { name: 'Delete sequence Daily farm?' });
    await expect(dialog.getByText('You cannot undo this.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Delete' }).click();
    await expect(dialog.getByText('Referenced by:')).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Delete' })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toHaveCount(0);

    await menuDelete(page, 'Night farm');
    await page.getByRole('dialog', { name: 'Delete sequence Night farm?' }).getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText('Sequence deleted.')).toBeVisible();
    await expect(page.locator('[data-testid^="sequence-row-"]')).toHaveCount(2);
  });

  test('a sequence with a name of 200 characters keeps the row height and a one-line title', async ({ page }) => {
    await open(page);
    near((await page.getByTestId('sequence-row-s2').boundingBox())!.height, 56);
    await page.getByTestId('sequence-row-s2').click();
    const title = page.getByRole('heading', { name: LONG_SEQUENCE_NAME });
    await expect(title).toBeVisible();
    const lineHeight = await title.evaluate((el) => parseFloat(getComputedStyle(el).lineHeight) || 30);
    expect((await title.boundingBox())!.height).toBeLessThan(lineHeight * 2);
  });

  test('S-11: no sideways scroll at 1024 x 768', async ({ page }) => {
    await open(page, { width: 1024, height: 768 });
    await noSidewaysScroll(page);
    await page.getByTestId('sequence-row-s2').click();
    await expect(page.getByRole('button', { name: 'Back' })).toBeVisible();
    await noSidewaysScroll(page);
  });
});
