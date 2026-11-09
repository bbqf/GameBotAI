import { expect, test, Page } from '@playwright/test';

/**
 * Feature 184 (SC-004): a sequence row of the template editor uses one line at a 1024 px window. Its height is at most half
 * of the old height. A jsdom test cannot measure layout, so this test runs in a real browser. All API routes are mocked.
 *
 * Old height: the row of an "Once per run" entry with the text buttons Parameters and Remove measured OLD_ROW_HEIGHT px at
 * a 1024 px window (measured one time on master, before feature 184).
 */
const OLD_ROW_HEIGHT = 114;

const json = { 'content-type': 'application/json' };
const ENTRY_COUNT = 10;

const entryIds = Array.from({ length: ENTRY_COUNT }, (_, i) => `e${i + 1}`);

const queueDetail = () => ({
  id: 'q1',
  name: 'PNS Daily',
  emulatorSerial: 'emulator-5558',
  cycleExecution: false,
  pauseWhenIdle: true,
  idleThresholdSeconds: 30,
  notificationLevel: 'failure',
  status: 'Running',
  entryCount: ENTRY_COUNT,
  linkedTemplateId: 't1',
  linkedTemplateName: 'Daily',
  linkedGameId: null,
  linkedGameName: null,
  operatorPause: 'none',
  entries: entryIds.map((id, i) => ({ entryId: id, sequenceId: `s${i + 1}`, sequenceName: `Sequence ${i + 1}`, stale: false })),
});

const templateDetail = () => ({
  id: 't1',
  name: 'Daily',
  entryCount: ENTRY_COUNT,
  createdAt: null,
  updatedAt: null,
  entries: entryIds.map((id, i) => ({
    entryId: id,
    sequenceId: `s${i + 1}`,
    sequenceName: `Sequence ${i + 1}`,
    stale: false,
    scheduleType: 'OncePerRun',
    timerTimeOfDay: null,
    timerRelativeOffset: null,
    enabled: true,
  })),
});

const monitor = () => ({
  queueId: 'q1', name: 'PNS Daily', running: true, cycleExecution: false, runStartedAt: null,
  current: null, upcoming: [], nothingScheduled: false, lastOutcome: null,
  entryStates: Object.fromEntries(entryIds.map((id) => [id, 'idle'])),
});

const mockApi = async (page: Page) => {
  await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) =>
    route.fulfill({ status: 200, headers: { 'content-type': 'text/css' }, body: '' }));
  await page.route('**/health', (route) =>
    route.fulfill({ status: 200, headers: json, body: JSON.stringify({ status: 'ok' }) }));
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname.toLowerCase();
    const ok = (body: unknown) => route.fulfill({ status: 200, headers: json, body: JSON.stringify(body) });
    if (path === '/api/queues') return ok([{ ...queueDetail(), entries: undefined }]);
    if (path === '/api/queues/q1') return ok(queueDetail());
    if (path === '/api/queues/q1/monitor') return ok(monitor());
    if (path === '/api/queue-templates/t1') return ok(templateDetail());
    if (path === '/api/queue-templates' || path === '/api/commands' || path === '/api/games') return ok([]);
    if (path === '/api/sequences') return ok(entryIds.map((_, i) => ({ id: `s${i + 1}`, name: `Sequence ${i + 1}`, steps: [] })));
    if (path === '/api/adb/devices') return ok([]);
    return ok({});
  });
};

test.describe('Template editor sequence row', () => {
  test('measures the row of a sequence at a 1024 px window', async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 900 });
    await mockApi(page);
    await page.goto('/?area=queues');
    await page.getByRole('button', { name: 'PNS Daily', exact: true }).click();

    const rows = page.getByTestId('queue-entry');
    await expect(rows).toHaveCount(ENTRY_COUNT);
    const heights: number[] = [];
    for (let i = 0; i < ENTRY_COUNT; i += 1) {
      const box = await rows.nth(i).boundingBox();
      heights.push(Math.round(box!.height));
    }
    // eslint-disable-next-line no-console
    console.log(`Sequence row heights: ${heights.join(', ')} px`);

    expect(OLD_ROW_HEIGHT, 'Set OLD_ROW_HEIGHT to the height of the old row (task T029)').toBeGreaterThan(0);
    for (const height of heights) {
      expect(height).toBeLessThanOrEqual(OLD_ROW_HEIGHT / 2);
    }
  });

  test('shows each action of the row as an icon button with a name and a tooltip', async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 900 });
    await mockApi(page);
    await page.goto('/?area=queues');
    await page.getByRole('button', { name: 'PNS Daily', exact: true }).click();

    const row = page.getByTestId('queue-entry').first();
    for (const name of ['Run next Sequence 1', 'Parameters for Sequence 1', 'Remove Sequence 1']) {
      const button = row.getByRole('button', { name });
      await expect(button).toBeVisible();
      await expect(button).toHaveAttribute('title', /.+/);
      await expect(button).toHaveText('');
    }
  });
});
