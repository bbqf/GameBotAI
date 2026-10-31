import { expect, test, Page } from '@playwright/test';

/**
 * Feature 173 (SC-002): the Actions column of the Queues list is at most half as wide as before.
 * A jsdom test cannot measure layout, so this test runs in a real browser. All API routes are mocked.
 *
 * Old width: the text buttons Pause, Stop, Schedule, Edit, Duplicate, and Delete measured 418 px at a 1280 px window.
 */
const OLD_ACTIONS_WIDTH = 418;

const json = { 'content-type': 'application/json' };

const queueDto = (status: 'Stopped' | 'Running') => ({
  id: 'q1',
  name: 'PNS Daily',
  emulatorSerial: 'emulator-5558',
  cycleExecution: false,
  pauseWhenIdle: true,
  idleThresholdSeconds: 30,
  notificationLevel: 'failure',
  status,
  entryCount: 2,
  linkedTemplateId: 't1',
  linkedGameId: null,
  linkedGameName: null,
  ...(status === 'Running' ? { operatorPause: 'none' } : {})
});

const mockApi = async (page: Page, status: 'Stopped' | 'Running') => {
  await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) =>
    route.fulfill({ status: 200, headers: { 'content-type': 'text/css' }, body: '' }));
  await page.route('**/health', (route) =>
    route.fulfill({ status: 200, headers: json, body: JSON.stringify({ status: 'ok' }) }));
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname.toLowerCase();
    const ok = (body: unknown) => route.fulfill({ status: 200, headers: json, body: JSON.stringify(body) });
    if (path === '/api/queues') return ok([queueDto(status)]);
    if (path === '/api/games') return ok([]);
    if (path === '/api/adb/devices') return ok([]);
    if (path === '/api/commands' || path === '/api/sequences' || path === '/api/queue-templates') return ok([]);
    return ok({});
  });
};

const measureActions = async (page: Page) => {
  const header = page.locator('table.queues-table th', { hasText: 'Actions' });
  await expect(header).toBeVisible();
  const headerBox = await header.boundingBox();
  const rowBox = await page.locator('table.queues-table tr.queues-row').first().boundingBox();
  return { width: headerBox!.width, rowHeight: rowBox!.height };
};

test.describe('Queues list Actions column', () => {
  test('measures the Actions column at a 1280 px window', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockApi(page, 'Running');
    await page.goto('/?area=queues');

    const { width, rowHeight } = await measureActions(page);
    // eslint-disable-next-line no-console
    console.log(`Actions column width: ${Math.round(width)} px, row height: ${Math.round(rowHeight)} px`);

    expect(OLD_ACTIONS_WIDTH, 'Set OLD_ACTIONS_WIDTH to the width of the old list (task T001)').toBeGreaterThan(0);
    expect(width).toBeLessThanOrEqual(OLD_ACTIONS_WIDTH / 2);
  });

  test('keeps the row on one line at a narrow window', async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 800 });
    await mockApi(page, 'Running');
    await page.goto('/?area=queues');

    const { rowHeight } = await measureActions(page);
    expect(rowHeight).toBeLessThanOrEqual(60);
  });
});
