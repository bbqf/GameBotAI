import { expect, test } from '@playwright/test';

const jsonHeaders = { 'content-type': 'application/json' };

const stubApiRoutes = async (page: import('@playwright/test').Page) => {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.toLowerCase();
    const listBodies: Record<string, unknown> = {
      '/api/commands': [{ id: 'cmd-1', name: 'Test Command' }],
      '/api/sequences': [],
      '/api/games': [],
      '/api/triggers': [],
      '/api/config': { host: '', token: '' },
    };
    if (path in listBodies) {
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(listBodies[path]) });
    } else if (route.request().method() === 'POST' && path.startsWith('/api/sequences')) {
      await route.fulfill({ status: 201, headers: jsonHeaders, body: JSON.stringify({ id: 'new-seq', name: 'Test', steps: [] }) });
    } else {
      await route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify({}) });
    }
  });
};

test.describe('Sequence loop step management', () => {
  test.beforeEach(async ({ page }) => {
    await stubApiRoutes(page);
    await page.goto('/');
    await page.getByRole('link', { name: /sequences/i }).click();
    await page.getByRole('button', { name: 'Create Sequence' }).click();
  });

  test('US1: persistent Add step button is visible and adds a top-level step outside any loop', async ({ page }) => {
    // Add a count loop so there is a loop in the sequence
    await page.getByTestId('add-loop-buttons').getByRole('button', { name: 'Count' }).click();

    // The persistent top-level "Add step" button must be present
    const addStepBtn = page.getByTestId('add-top-level-step').first();
    await expect(addStepBtn).toBeVisible();

    // Click it — should append a step at the top-level sequence scope
    await addStepBtn.click();

    // There should now be a step item outside the loop-body (loop-block itself + new step = 2 items in the list)
    const stepItems = page.locator('.reorderable-list__item');
    await expect(stepItems).toHaveCount(2);
  });

  test('US1: Add step button is accessible even when sequence contains only a loop', async ({ page }) => {
    await page.getByTestId('add-loop-buttons').getByRole('button', { name: 'Count' }).click();
    const addStepBtn = page.getByTestId('add-top-level-step').first();
    await expect(addStepBtn).toBeVisible();
    await expect(addStepBtn).toBeEnabled();
  });

  test('US2: drag handles are present on top-level steps', async ({ page }) => {
    // Add two steps then a loop
    await page.getByTestId('add-top-level-step').first().click();
    await page.getByTestId('add-loop-buttons').getByRole('button', { name: 'Count' }).click();

    // All top-level items should have a drag handle
    const handles = page.locator('.sortable-step-item__handle');
    const count = await handles.count();
    expect(count).toBeGreaterThanOrEqual(2);
  });

  test('US2: cross-scope drag shows loop-block--drop-invalid class when dragging top-level step over loop body', async ({ page }) => {
    // Add a top-level step, then a loop
    await page.getByTestId('add-top-level-step').first().click();
    await page.getByTestId('add-loop-buttons').getByRole('button', { name: 'Count' }).click();

    const handle = page.locator('.reorderable-list__item').first().locator('.sortable-step-item__handle');
    const loopBody = page.getByTestId('loop-body');

    // Start dragging the top-level step over the loop body
    await handle.hover();
    await page.mouse.down();
    const loopBodyBox = await loopBody.boundingBox();
    if (loopBodyBox) {
      await page.mouse.move(loopBodyBox.x + loopBodyBox.width / 2, loopBodyBox.y + loopBodyBox.height / 2, { steps: 5 });
      await expect(loopBody).toHaveClass(/loop-block--drop-invalid/);
    }
    await page.mouse.up();
  });

  test('US3: in-loop Add step button still adds steps inside the loop after refactor', async ({ page }) => {
    // Add a loop and use its internal Add step button
    await page.getByTestId('add-loop-buttons').getByRole('button', { name: 'Count' }).click();

    const addBodyStepBtn = page.getByTestId('add-body-step');
    await expect(addBodyStepBtn).toBeVisible();
    await addBodyStepBtn.click();

    // The loop body should contain exactly one step
    const loopBodySteps = page.getByTestId('loop-body-step');
    await expect(loopBodySteps).toHaveCount(1);
  });
});
