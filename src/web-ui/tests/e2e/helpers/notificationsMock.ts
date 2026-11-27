import type { Page } from '@playwright/test';

/**
 * A mocked API for the Notifications area tests (feature 203). It holds 3 targets and 3 queues, and a list of the calls.
 */
export const json = { 'content-type': 'application/json' };

export const LONG_TARGET_NAME = 'Very long target name '.repeat(10).trim().slice(0, 100);

export type MockTarget = {
  id: string;
  type: string;
  name: string;
  enabled: boolean;
  settings: Record<string, string>;
  hasSecret: boolean;
  secretHint: string | null;
};

export type MockLevelQueue = { id: string; name: string; notificationLevel: 'none' | 'failure' | 'successAndFailure' };

export type NotificationsMockState = {
  targets: MockTarget[];
  queues: MockLevelQueue[];
  calls: string[];
  /** Milliseconds to wait before the answer of the test call. */
  testDelayMs: number;
};

export const defaultTargets = (): MockTarget[] => [
  { id: 't1', type: 'telegram', name: 'My phone', enabled: true, settings: { chatId: '-100123' }, hasSecret: true, secretHint: '••••Xy9z' },
  { id: 't2', type: 'telegram', name: 'Team chat', enabled: false, settings: { chatId: '-100456' }, hasSecret: false, secretHint: null },
  { id: 't3', type: 'telegram', name: LONG_TARGET_NAME, enabled: true, settings: { chatId: '-100789' }, hasSecret: true, secretHint: null },
];

export const defaultLevelQueues = (): MockLevelQueue[] => [
  { id: 'q1', name: 'Farm-1', notificationLevel: 'none' },
  { id: 'q2', name: 'Farm-2', notificationLevel: 'failure' },
  { id: 'q3', name: 'Farm-3', notificationLevel: 'successAndFailure' },
];

const queueDto = (q: MockLevelQueue) => ({
  id: q.id,
  name: q.name,
  emulatorSerial: 'emulator-5558',
  cycleExecution: false,
  pauseWhenIdle: true,
  idleThresholdSeconds: 30,
  notificationLevel: q.notificationLevel,
  status: 'Stopped',
  entryCount: 0,
  linkedTemplateId: null,
  linkedGameId: null,
  linkedGameName: null,
});

export const mockNotificationsApi = async (page: Page): Promise<NotificationsMockState> => {
  const state: NotificationsMockState = { targets: defaultTargets(), queues: defaultLevelQueues(), calls: [], testDelayMs: 0 };

  await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) =>
    route.fulfill({ status: 200, headers: { 'content-type': 'text/css' }, body: '' }));
  await page.route('**/health', (route) =>
    route.fulfill({ status: 200, headers: json, body: JSON.stringify({ status: 'ok' }) }));
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.toLowerCase();
    const method = request.method();
    state.calls.push(`${method} ${path}`);
    const ok = (body: unknown) => route.fulfill({ status: 200, headers: json, body: JSON.stringify(body) });

    if (path === '/api/notifications/targets') {
      if (method === 'GET') return ok(state.targets);
      if (method === 'POST') {
        const input = request.postDataJSON();
        const saved: MockTarget = {
          id: `t${state.targets.length + 10}`,
          type: input.type,
          name: input.name,
          enabled: input.enabled,
          settings: input.settings,
          hasSecret: Boolean(input.secrets?.botToken),
          secretHint: null,
        };
        state.targets.push(saved);
        return ok(saved);
      }
    }
    const targetMatch = /^\/api\/notifications\/targets\/([^/]+)(?:\/(test))?$/.exec(path);
    if (targetMatch) {
      const id = targetMatch[1];
      const found = state.targets.find((t) => t.id === id);
      if (targetMatch[2] === 'test' && method === 'POST') {
        if (state.testDelayMs > 0) await new Promise((resolve) => setTimeout(resolve, state.testDelayMs));
        return ok({ ok: true, reason: null });
      }
      if (method === 'DELETE') {
        state.targets = state.targets.filter((t) => t.id !== id);
        return route.fulfill({ status: 204 });
      }
      if (method === 'PUT' && found) {
        const input = request.postDataJSON();
        Object.assign(found, { name: input.name, enabled: input.enabled, settings: input.settings });
        return ok(found);
      }
    }
    if (path === '/api/queues' && method === 'GET') return ok(state.queues.map(queueDto));
    const levelMatch = /^\/api\/queues\/([^/]+)\/notification-level$/.exec(path);
    if (levelMatch && method === 'PUT') {
      const queue = state.queues.find((q) => q.id === levelMatch[1]);
      if (!queue) return route.fulfill({ status: 404, headers: json, body: JSON.stringify({ message: 'Not found' }) });
      queue.notificationLevel = request.postDataJSON().level;
      return ok(queueDto(queue));
    }
    if (path === '/api/games' || path === '/api/adb/devices') return ok([]);
    if (path === '/api/commands' || path === '/api/sequences' || path === '/api/queue-templates') return ok([]);
    return ok({});
  });

  return state;
};
