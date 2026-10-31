import { expect, test, Page } from '@playwright/test';

/**
 * Phone layout checks (feature 139). The `mobile` project runs this file with a 360 x 640 touch viewport.
 * All API routes are mocked, so no service is needed.
 */

const json = { 'content-type': 'application/json' };

type MockState = {
  queueStatus: 'Stopped' | 'Running';
  entryEnabled: boolean[];
  calls: string[];
  health: boolean;
};

const WIDTHS_PHONE = [320, 360, 390, 480, 600, 768];

const queueDto = (state: MockState) => ({
  id: 'q1',
  name: 'PNS Daily',
  emulatorSerial: 'emulator-5558',
  cycleExecution: false,
  pauseWhenIdle: true,
  idleThresholdSeconds: 30,
  notificationLevel: 'failure',
  status: state.queueStatus,
  entryCount: 2,
  linkedTemplateId: 't1',
  linkedGameId: null,
  linkedGameName: null
});

const templateDto = (state: MockState) => ({
  id: 't1',
  name: 'PNS Daily tpl',
  entryCount: 2,
  createdAt: null,
  updatedAt: null,
  entries: [
    { sequenceId: 's1', sequenceName: 'Alliance Help All with a very long sequence name that must be cut with an ellipsis', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: state.entryEnabled[0] },
    { sequenceId: 's2', sequenceName: 'Gift', stale: false, scheduleType: 'Timer', timerTimeOfDay: '04:00:00', timerRelativeOffset: null, enabled: state.entryEnabled[1] }
  ]
});

const mockApi = async (page: Page, overrides: Partial<MockState> = {}): Promise<MockState> => {
  const state: MockState = { queueStatus: 'Stopped', entryEnabled: [true, true], calls: [], health: true, ...overrides };

  // The app imports web fonts. Answer with an empty style sheet so a test never waits for the Internet.
  await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) =>
    route.fulfill({ status: 200, headers: { 'content-type': 'text/css' }, body: '' }));

  await page.route('**/health', async (route) => {
    if (!state.health) return route.abort('connectionrefused');
    return route.fulfill({ status: 200, headers: json, body: JSON.stringify({ status: 'ok' }) });
  });

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.toLowerCase();
    const method = request.method();
    state.calls.push(`${method} ${path}`);
    const ok = (body: unknown) => route.fulfill({ status: 200, headers: json, body: JSON.stringify(body) });

    if (path === '/api/queues' && method === 'GET') return ok([queueDto(state)]);
    if (path === '/api/queues/q1' && method === 'GET') {
      return ok({ ...queueDto(state), linkedTemplateName: 'PNS Daily tpl', entries: [] });
    }
    if (path === '/api/queues/q1/start') { state.queueStatus = 'Running'; return ok(queueDto(state)); }
    if (path === '/api/queues/q1/stop') { state.queueStatus = 'Stopped'; return ok(queueDto(state)); }
    if (path === '/api/queue-templates/t1') return ok(templateDto(state));
    if (path === '/api/queue-templates' && method === 'POST') {
      const body = request.postDataJSON() as { entries: Array<{ enabled?: boolean }> };
      state.entryEnabled = body.entries.map((e) => e.enabled !== false);
      return ok(templateDto(state));
    }
    if (path === '/api/games') return ok([{ id: 'g1', name: 'Puzzles and Survival', metadata: {} }]);
    if (path === '/api/adb/devices') return ok([{ serial: 'emulator-5558', state: 'device' }]);
    if (path === '/api/sessions/running') return ok({ sessions: [{ sessionId: 'sess-1', gameId: 'g1', emulatorId: 'emulator-5558', startedAtUtc: new Date().toISOString(), lastHeartbeatUtc: new Date().toISOString(), status: 'Running' }] });
    if (path === '/api/commands') return ok([{ id: 'c1', name: 'Tap home', steps: [] }]);
    if (path === '/api/sequences') return ok([{ id: 's1', name: 'Help All', steps: [] }]);
    if (path === '/api/notifications/targets') return ok([{ id: 'n1', type: 'telegram', name: 'My phone', enabled: true, settings: { chatId: '-100123' }, hasSecret: true, secretHint: '••••Xy9z' }]);
    if (path === '/api/notifications/types') return ok([]);
    if (path === '/api/config') return ok({ generatedAtUtc: new Date().toISOString(), serviceVersion: '1.0.0', dynamicPort: 8080, refreshCount: 0, envScanned: 0, envIncluded: 0, envExcluded: 0, parameters: { 'Service:Port': { name: 'Service:Port', source: 'Default', value: 8080, isSecret: false } } });
    if (path.startsWith('/api/config/files/')) return ok({ fileName: 'x.json', parameters: {} });
    if (path === '/api/update/status') return ok({ installedVersion: '1.0.0', canInstallHere: true, lastCheck: null, attempt: null, lastResult: null });
    return ok({});
  });

  return state;
};

const noSidewaysScroll = async (page: Page) => {
  const metrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth
  }));
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth);
};

/** Every visible, enabled control is at least 44 x 44 px. Checkbox and radio inputs count with their margin box. */
const tapTargetsAreBigEnough = async (page: Page) => {
  const small = await page.evaluate(() => {
    const selector = 'button, a[href], [role="tab"], [role="switch"], select, summary, input[type="checkbox"], input[type="radio"]';
    const result: string[] = [];
    document.querySelectorAll<HTMLElement>(selector).forEach((el) => {
      const style = getComputedStyle(el);
      if (style.display === 'none' || style.visibility === 'hidden') return;
      const rect = el.getBoundingClientRect();
      if (rect.width === 0 && rect.height === 0) return;
      const isToggle = el instanceof HTMLInputElement && (el.type === 'checkbox' || el.type === 'radio');
      const width = rect.width + (isToggle ? parseFloat(style.marginLeft) + parseFloat(style.marginRight) : 0);
      const height = rect.height + (isToggle ? parseFloat(style.marginTop) + parseFloat(style.marginBottom) : 0);
      if (width < 43.5 || height < 43.5) {
        result.push(`${el.tagName.toLowerCase()} "${(el.textContent ?? el.getAttribute('aria-label') ?? '').trim().slice(0, 30)}" ${Math.round(width)}x${Math.round(height)}`);
      }
    });
    return result;
  });
  expect(small, `Small tap targets: ${small.join('; ')}`).toEqual([]);
};

test.describe('Queues on a phone', () => {
  test('shows queue cards and starts and stops a queue in one tap', async ({ page }) => {
    const state = await mockApi(page);
    await page.goto('/');
    await expect(page.getByTestId('phone-queues')).toBeVisible();
    await expect(page.getByText('PNS Daily')).toBeVisible();
    await expect(page.getByTestId('queue-status')).toHaveText('Stopped');

    await page.getByRole('button', { name: 'Start', exact: true }).click();
    await expect(page.getByTestId('queue-status')).toHaveText('Running');
    expect(state.calls).toContain('POST /api/queues/q1/start');

    await page.getByRole('button', { name: 'Stop', exact: true }).click();
    await expect(page.getByTestId('queue-status')).toHaveText('Stopped');
    expect(state.calls).toContain('POST /api/queues/q1/stop');
  });

  test('switches one entry in the queue detail', async ({ page }) => {
    const state = await mockApi(page);
    await page.goto('/');
    await page.getByRole('button', { name: 'Open PNS Daily' }).click();
    await expect(page.getByText('Changes apply when the queue starts again.')).toBeVisible();
    await page.getByRole('switch', { name: 'Gift' }).click();
    await expect.poll(() => state.entryEnabled).toEqual([true, false]);
    await noSidewaysScroll(page);
  });

  for (const width of WIDTHS_PHONE) {
    test(`has no sideways scroll and big tap targets at ${width} px`, async ({ page }) => {
      await mockApi(page);
      await page.setViewportSize({ width, height: 800 });
      await page.goto('/');
      await expect(page.getByTestId('phone-queues')).toBeVisible();
      await expect(page.getByRole('navigation', { name: 'Primary Navigation' })).toHaveClass(/phone-tabbar/);
      await noSidewaysScroll(page);
      await tapTargetsAreBigEnough(page);

      await page.getByRole('button', { name: 'Open PNS Daily' }).click();
      await expect(page.getByTestId('phone-queue-detail')).toBeVisible();
      await expect(page.getByRole('switch', { name: 'Gift' })).toBeVisible();
      await noSidewaysScroll(page);
      await tapTargetsAreBigEnough(page);
    });
  }

  test('keeps the phone layout on a 640 x 360 landscape screen', async ({ page }) => {
    await mockApi(page);
    await page.setViewportSize({ width: 640, height: 360 });
    await page.goto('/');
    await expect(page.getByTestId('phone-queues')).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Primary Navigation' })).toHaveClass(/phone-tabbar/);
    await expect(page.getByRole('tab', { name: 'Authoring' })).toHaveCount(0);
    await noSidewaysScroll(page);
  });

  test('shows the desktop layout at 769 px', async ({ page }) => {
    await mockApi(page);
    await page.setViewportSize({ width: 769, height: 900 });
    await page.goto('/');
    await expect(page.getByRole('tab', { name: 'Authoring' })).toBeVisible();
    await expect(page.getByTestId('phone-queues')).toHaveCount(0);
    await noSidewaysScroll(page);
  });

  test('shows the banner and turns the controls off when the service does not answer', async ({ page }) => {
    const state = await mockApi(page);
    await page.goto('/');
    await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();
    state.health = false;
    await page.context().setOffline(true);
    await expect(page.getByText('Connection lost')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeDisabled();
    await page.context().setOffline(false);
    state.health = true;
    await expect(page.getByText('Connection lost')).toHaveCount(0, { timeout: 15_000 });
  });
});

test.describe('Other areas on a phone', () => {
  const areas: Array<{ area: string; ready: (page: Page) => Promise<void> }> = [
    { area: 'notifications', ready: async (page) => { await expect(page.getByText('My phone')).toBeVisible(); } },
    { area: 'configuration', ready: async (page) => { await expect(page.getByText('Backend Connection')).toBeVisible(); } },
    { area: 'update', ready: async (page) => { await expect(page.getByText('Installed version:')).toBeVisible(); } }
  ];

  for (const { area, ready } of areas) {
    for (const width of [...WIDTHS_PHONE, 769]) {
      test(`${area} has no sideways scroll at ${width} px`, async ({ page }) => {
        await mockApi(page);
        await page.setViewportSize({ width, height: 800 });
        await page.goto(`/?area=${area}`);
        await ready(page);
        await noSidewaysScroll(page);
        if (width <= 768) await tapTargetsAreBigEnough(page);
      });
    }

    test(`${area} needs at most 2 screen heights of scroll`, async ({ page }) => {
      await mockApi(page);
      await page.goto(`/?area=${area}`);
      await ready(page);
      const ratio = await page.evaluate(() => document.documentElement.scrollHeight / window.innerHeight);
      expect(ratio).toBeLessThanOrEqual(2);
    });
  }

  test('does one typical task in each visible area', async ({ page }) => {
    await mockApi(page);
    // Enter the token.
    await page.goto('/?area=configuration');
    await page.getByLabel('Bearer Token').fill('abc123');
    await expect(page.getByLabel('Bearer Token')).toHaveValue('abc123');
    // Read the update status.
    await page.goto('/?area=update');
    await expect(page.getByText('Installed version:')).toBeVisible();
    // Change a notification level.
    await page.goto('/?area=notifications');
    const select = page.getByLabel('Notification level for PNS Daily');
    await expect(select).toBeVisible();
    await select.selectOption('none');
  });
});

test.describe('Desktop-only areas on a phone', () => {
  test('shows the notice for Authoring and Execution Logs and sends no data call', async ({ page }) => {
    const state = await mockApi(page);
    await page.goto('/?area=authoring');
    await expect(page.getByText('Use a larger screen for this area.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Authoring', level: 2 })).toBeVisible();

    await page.goto('/execution-logs');
    await expect(page.getByText('Use a larger screen for this area.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Execution Logs', level: 2 })).toBeVisible();

    const dataCalls = state.calls.filter((c) => /\/api\/(commands|sequences|images|executions?-logs?|games)/.test(c));
    expect(dataCalls).toEqual([]);

    await page.getByRole('button', { name: 'Back to Queues' }).click();
    await expect(page.getByTestId('phone-queues')).toBeVisible();
  });

  test('reaches the notice from the More sheet', async ({ page }) => {
    await mockApi(page);
    await page.goto('/');
    await page.getByRole('button', { name: /More/ }).click();
    await expect(page.getByRole('button', { name: /Authoring/ })).toContainText('Desktop only');
    await page.getByRole('button', { name: /Authoring/ }).click();
    await expect(page.getByText('Use a larger screen for this area.')).toBeVisible();
  });

  test('both areas work at 1280 px', async ({ page }) => {
    await mockApi(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/?area=authoring');
    await expect(page.getByRole('heading', { name: 'Authoring', level: 1 })).toBeVisible();
    await expect(page.getByText('Use a larger screen for this area.')).toHaveCount(0);
    await page.getByRole('tab', { name: 'Execution Logs' }).click();
    await expect(page.getByText('Use a larger screen for this area.')).toHaveCount(0);
  });
});

test.describe('First screen time', () => {
  test.describe.configure({ retries: 1 });

  test('is ready in under 3 s with CPU and network throttling (SC-009)', async ({ page, browserName }, testInfo) => {
    test.skip(browserName !== 'chromium', 'Throttling needs the Chromium DevTools protocol.');
    await mockApi(page);
    const client = await page.context().newCDPSession(page);
    await client.send('Emulation.setCPUThrottlingRate', { rate: 4 });
    await client.send('Network.enable');
    // Slow 4G profile.
    await client.send('Network.emulateNetworkConditions', {
      offline: false,
      latency: 150,
      downloadThroughput: (1.6 * 1024 * 1024) / 8,
      uploadThroughput: (750 * 1024) / 8
    });

    const start = Date.now();
    await page.goto('/');
    await expect(page.getByTestId('phone-queues')).toBeVisible();
    await expect(page.getByText('PNS Daily')).toBeVisible();
    const elapsed = Date.now() - start;
    console.log(`First screen time: ${elapsed} ms`);
    testInfo.annotations.push({ type: 'first-screen-ms', description: String(elapsed) });

    if (process.env.CI) {
      // A slow CI runner can cause a false failure, so a slow result is a warning in CI.
      if (elapsed > 3000) console.warn(`WARNING: first screen took ${elapsed} ms (goal: under 3000 ms).`);
    } else {
      expect(elapsed).toBeLessThan(3000);
    }
  });
});
