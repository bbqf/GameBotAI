import type { StepThroughHistoryEntryDto, StepThroughNodeDto, StepThroughStateDto } from '../../services/stepThrough';

/** Test data for the step-through view. It is not part of the application. */

export const sampleNodes = (): StepThroughNodeDto[] => [
  { path: '0', depth: 0, stepId: 'a', type: 'action', label: 'tap 10,20', container: false, selectable: true },
  { path: '1', depth: 0, stepId: 'loop', type: 'loop', label: 'loop 2 times', container: true, selectable: false },
  { path: '1/body/0', depth: 1, stepId: 'in', type: 'command', label: 'command open-menu', container: false, selectable: true, branch: 'body' },
  { path: '2', depth: 0, stepId: 'c', type: 'action', label: 'tap 30,40', container: false, selectable: true }
];

export const entry = (overrides: Partial<StepThroughHistoryEntryDto> = {}): StepThroughHistoryEntryDto => ({
  seq: 1,
  path: '0',
  stepId: 'a',
  kind: 'step',
  iteration: null,
  status: 'Succeeded',
  outcome: 'executed',
  message: null,
  effects: [],
  notes: [],
  startedAt: '2026-10-02T10:00:00Z',
  durationMs: 120,
  executionLogId: 'log-1',
  ...overrides
});

export const makeState = (overrides: Partial<StepThroughStateDto> = {}): StepThroughStateDto => ({
  id: 'st-1',
  sequenceId: 'seq-1',
  sequenceName: 'Daily sequence',
  gameSessionId: 'gs-1',
  state: 'idle',
  cursor: '0',
  nodes: sampleNodes(),
  running: null,
  history: [],
  parameters: [],
  outcomes: {},
  queue: null,
  leaseExpiresAt: '2026-10-02T10:01:30Z',
  ...overrides
});

export const runningSession = (id = 'gs-1') => ({
  sessionId: id,
  gameId: 'pns',
  emulatorId: 'emulator-5558',
  startedAtUtc: '2026-10-02T09:00:00Z',
  lastHeartbeatUtc: '2026-10-02T10:00:00Z',
  status: 'Running' as const
});
