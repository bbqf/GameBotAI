import type { Page } from '@playwright/test';

/**
 * A mocked API for the Queues list tests (feature 202). It holds 5 queues and a list of the calls.
 * A test can change `state.queues` to change the answer of the next list call.
 */
export const json = { 'content-type': 'application/json' };

export const LONG_NAME = 'Very long queue name '.repeat(10).trim().slice(0, 200);

export type MockQueue = {
  id: string;
  name: string;
  status: 'Stopped' | 'Running';
  operatorPause?: 'none' | 'pausing' | 'paused';
  deviceLiveness?: 'live' | 'not_live';
};

export type QueuesMockState = {
  queues: MockQueue[];
  calls: string[];
  /** Milliseconds to wait before the answer of the list call. */
  listDelayMs: number;
};

export const defaultQueues = (): MockQueue[] => [
  { id: 'q1', name: 'PNS Daily', status: 'Stopped' },
  { id: 'q2', name: 'Running Queue', status: 'Running', operatorPause: 'none' },
  { id: 'q3', name: 'Paused Queue', status: 'Running', operatorPause: 'paused' },
  { id: 'q4', name: LONG_NAME, status: 'Stopped' },
  { id: 'q5', name: 'Offline Queue', status: 'Stopped', deviceLiveness: 'not_live' },
];

const toDto = (q: MockQueue) => ({
  id: q.id,
  name: q.name,
  emulatorSerial: 'emulator-5558',
  cycleExecution: false,
  pauseWhenIdle: true,
  idleThresholdSeconds: 30,
  notificationLevel: 'failure',
  status: q.status,
  entryCount: 2,
  linkedTemplateId: null,
  linkedGameId: null,
  linkedGameName: null,
  ...(q.status === 'Running' ? { operatorPause: q.operatorPause ?? 'none' } : {}),
  ...(q.deviceLiveness ? { deviceLiveness: q.deviceLiveness } : {}),
});

export const mockQueuesApi = async (page: Page, queues: MockQueue[] = defaultQueues()): Promise<QueuesMockState> => {
  const state: QueuesMockState = { queues, calls: [], listDelayMs: 0 };

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
    const find = (id: string) => state.queues.find((q) => q.id === id);

    if (path === '/api/queues' && method === 'GET') {
      if (state.listDelayMs > 0) await new Promise((resolve) => setTimeout(resolve, state.listDelayMs));
      return ok(state.queues.map(toDto));
    }
    const match = /^\/api\/queues\/([^/]+)(?:\/(\w+))?$/.exec(path);
    if (match) {
      const queue = find(match[1]);
      const action = match[2];
      if (!queue) return route.fulfill({ status: 404, headers: json, body: JSON.stringify({ message: 'Not found' }) });
      if (method === 'DELETE') {
        state.queues = state.queues.filter((q) => q.id !== queue.id);
        return route.fulfill({ status: 204 });
      }
      if (!action && method === 'GET') return ok({ ...toDto(queue), linkedTemplateName: null, entries: [] });
      if (action === 'start') { queue.status = 'Running'; queue.operatorPause = 'none'; return ok(toDto(queue)); }
      if (action === 'stop') { queue.status = 'Stopped'; return ok(toDto(queue)); }
      if (action === 'monitor') {
        return ok({ queueId: queue.id, name: queue.name, running: queue.status === 'Running', cycleExecution: false, runStartedAt: null, current: null, upcoming: [], nothingScheduled: false, lastOutcome: null });
      }
    }
    if (path === '/api/games') return ok([]);
    if (path === '/api/adb/devices') return ok([]);
    if (path === '/api/commands' || path === '/api/sequences' || path === '/api/queue-templates') return ok([]);
    return ok({});
  });

  return state;
};