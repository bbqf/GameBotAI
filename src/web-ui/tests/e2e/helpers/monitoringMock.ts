import type { Page } from '@playwright/test';

/**
 * A mocked API for the Monitoring tests (feature 204). It holds 3 sessions and a list of the calls.
 */
export const json = { 'content-type': 'application/json' };

const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64'
);

export type MonitoringMockState = {
  calls: string[];
};

const sessions = () => [
  {
    id: 'a', name: 'Device A', gameId: 'pns', deviceSerial: 'emulator-5554', state: 'live', reason: null,
    ownerQueueId: 'q1', ownerQueueName: 'Farm gold', currentSequenceName: 'Daily rewards',
    ownerQueueDeviceLiveness: 'live', restartAvailable: true, restartUnavailableReason: null,
    startedAtUtc: '2026-11-27T08:00:00Z'
  },
  {
    id: 'b', name: 'Device B', gameId: 'pns', deviceSerial: 'emulator-5556', state: 'no_answer',
    reason: 'No change after input', ownerQueueId: null, ownerQueueName: null, currentSequenceName: null,
    ownerQueueDeviceLiveness: null, restartAvailable: true, restartUnavailableReason: null,
    startedAtUtc: '2026-11-27T08:05:00Z'
  },
  {
    id: 'c', name: 'Device C', gameId: 'pns', deviceSerial: 'emulator-5558', state: 'live', reason: null,
    ownerQueueId: 'q2', ownerQueueName: 'Arena', currentSequenceName: null,
    ownerQueueDeviceLiveness: 'not_live', restartAvailable: false,
    restartUnavailableReason: 'The emulator is not an LDPlayer instance.',
    startedAtUtc: '2026-11-27T08:10:00Z'
  }
];

export const mockMonitoringApi = async (page: Page): Promise<MonitoringMockState> => {
  const state: MonitoringMockState = { calls: [] };

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

    if (path === '/api/sessions/monitoring' && method === 'GET') return ok({ sessions: sessions() });
    const match = /^\/api\/sessions\/([^/]+)\/(snapshot|restart)$/.exec(path);
    if (match) {
      if (match[2] === 'snapshot') return route.fulfill({ status: 200, headers: { 'content-type': 'image/png' }, body: PNG_1X1 });
      const record = {
        sessionId: match[1], state: 'restarting', step: 'rebooting',
        startedAtUtc: '2026-11-27T09:00:00Z', endedAtUtc: null, reason: null
      };
      if (method === 'POST') return ok(record);
      const started = state.calls.some((c) => c === `POST /api/sessions/${match[1]}/restart`);
      return started ? ok(record) : route.fulfill({ status: 404, headers: json, body: JSON.stringify({ message: 'Not found' }) });
    }
    return ok({});
  });

  return state;
};
