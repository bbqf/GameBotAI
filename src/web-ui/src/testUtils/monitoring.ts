import type { MonitoredSessionDto, SessionRestartRecordDto } from '../services/monitoringApi';

/** A monitored session for tests (feature 141). Fields can be overridden. */
export const makeSession = (id: string, overrides: Partial<MonitoredSessionDto> = {}): MonitoredSessionDto => ({
  id,
  name: `pns (emulator-${id})`,
  gameId: 'pns',
  deviceSerial: `emulator-${id}`,
  state: 'live',
  reason: null,
  ownerQueueId: null,
  ownerQueueName: null,
  restartAvailable: true,
  restartUnavailableReason: null,
  startedAtUtc: '2026-10-13T08:00:00Z',
  ...overrides
});

/** A restart record for tests. */
export const makeRecord = (sessionId: string, overrides: Partial<SessionRestartRecordDto> = {}): SessionRestartRecordDto => ({
  sessionId,
  state: 'restarting',
  step: 'rebooting',
  startedAtUtc: '2026-10-13T08:10:00Z',
  endedAtUtc: null,
  reason: null,
  ...overrides
});

/** Gives jsdom the object URL functions that the screenshot view needs. */
export const installObjectUrlMock = (): void => {
  let counter = 0;
  Object.defineProperty(URL, 'createObjectURL', { configurable: true, writable: true, value: jest.fn(() => `blob:test-${++counter}`) });
  Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, writable: true, value: jest.fn() });
};
