import { expect, test, Page } from '@playwright/test';

/**
 * Feature 185 (rule E-10, mobile principles 1, 2, and 3): the phone entry page has no sideways scroll, and each control is at
 * least 44 x 44 px. Each text input uses a font of at least 16 px. A jsdom test cannot measure layout, so this test runs
 * in a real browser. All API routes are mocked.
 */
const json = { 'content-type': 'application/json' };

const SIZES: Array<[number, number]> = [
  [320, 640], [360, 640], [390, 844], [480, 800], [600, 900], [768, 1024], [640, 360],
];

const queueDetail = () => ({
  id: 'q1',
  name: 'PNS Daily with a long queue name that must wrap on a narrow phone',
  emulatorSerial: 'emulator-5558',
  cycleExecution: false,
  pauseWhenIdle: true,
  idleThresholdSeconds: 30,
  notificationLevel: 'failure',
  status: 'Stopped',
  entryCount: 1,
  linkedTemplateId: 't1',
  linkedTemplateName: 'Daily',
  linkedGameId: null,
  linkedGameName: null,
  operatorPause: 'none',
  entries: [{ entryId: 'e1', sequenceId: 's1', sequenceName: 'Collect daily rewards from all', stale: false }],
});

const templateDetail = () => ({
  id: 't1',
  name: 'Daily',
  entryCount: 1,
  createdAt: null,
  updatedAt: null,
  entries: [{
    entryId: 'e1',
    sequenceId: 's1',
    sequenceName: 'Collect daily rewards from all',
    stale: false,
    scheduleType: 'Timer',
    timerTimeOfDay: '14:30:00',
    timerRelativeOffset: null,
    enabled: true,
    parameterValues: [{ name: 'count', value: '5' }, { name: 'extraName', value: 'x' }],
    effectiveParameters: [
      { name: 'count', value: '5', originLayer: 'entry', declared: true },
      { name: 'label', value: 'daily', originLayer: 'default', declared: true },
    ],
  }],
});

const sequence = () => ({
  id: 's1',
  name: 'Collect daily rewards from all',
  steps: [],
  parameters: [
    { name: 'count', type: 'number', default: '3', required: false },
    { name: 'label', type: 'text', default: 'daily', required: false },
  ],
});

const monitor = () => ({
  queueId: 'q1', name: 'PNS Daily', running: false, cycleExecution: false, runStartedAt: null,
  current: null, upcoming: [], nothingScheduled: false, lastOutcome: null,
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
    if (path === '/api/sequences/s1') return ok(sequence());
    if (path === '/api/queue-templates' || path === '/api/commands' || path === '/api/games') return ok([]);
    if (path === '/api/adb/devices') return ok([]);
    return ok({});
  });
};

test.describe('Phone entry page layout', () => {
  for (const [width, height] of SIZES) {
    test(`has no sideways scroll, targets of 44 px, and inputs of 16 px at ${width} x ${height}`, async ({ page }) => {
      await page.setViewportSize({ width, height });
      await mockApi(page);
      await page.goto('/?area=queues&queue=q1&entry=e1');

      const entryPage = page.getByTestId('phone-entry-page');
      await expect(entryPage).toBeVisible();
      await expect(page.getByLabel('Parameter values')).toBeVisible();

      // E-10: the page does not scroll sideways.
      const overflow = await page.evaluate(() => ({
        scroll: document.documentElement.scrollWidth,
        client: document.documentElement.clientWidth,
        body: document.body.scrollWidth,
      }));
      expect(overflow.scroll).toBeLessThanOrEqual(overflow.client);
      expect(overflow.body).toBeLessThanOrEqual(overflow.client);

      // Mobile principle 3: each visible control is at least 44 x 44 px.
      const small = await entryPage.evaluate((root) => {
        const result: string[] = [];
        root.querySelectorAll('button, input, select, textarea, [role="switch"]').forEach((el) => {
          const box = (el as HTMLElement).getBoundingClientRect();
          if (box.width === 0 || box.height === 0) return;
          // The app makes a checkbox 24 px with a 10 px margin. The margin is part of the hit area (44 px).
          const style = getComputedStyle(el);
          const box44 = el.matches('input[type="checkbox"], input[type="radio"]');
          const width = box.width + (box44 ? parseFloat(style.marginLeft) + parseFloat(style.marginRight) : 0);
          const height = box.height + (box44 ? parseFloat(style.marginTop) + parseFloat(style.marginBottom) : 0);
          if (Math.round(width) < 44 || Math.round(height) < 44) {
            result.push(`${el.tagName.toLowerCase()} ${el.getAttribute('aria-label') ?? el.textContent ?? ''} ${Math.round(width)}x${Math.round(height)}`);
          }
        });
        return result;
      });
      expect(small, `Controls under 44 x 44 px at ${width} x ${height}`).toEqual([]);

      // Mobile principle 2: each text input uses at least 16 px.
      const smallFonts = await entryPage.evaluate((root) => {
        const result: string[] = [];
        root.querySelectorAll('input[type="text"], input[type="number"], select, textarea').forEach((el) => {
          const size = parseFloat(getComputedStyle(el).fontSize);
          if (size < 16) result.push(`${el.getAttribute('aria-label') ?? el.tagName} ${size}px`);
        });
        return result;
      });
      expect(smallFonts, `Inputs under 16 px at ${width} x ${height}`).toEqual([]);
    });
  }
});
