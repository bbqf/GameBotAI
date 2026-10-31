import React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../pages/QueuesPage';
import { PhoneQueuesView } from '../components/queues/PhoneQueuesView';
import * as queuesApi from '../services/queues';
import * as sequencesApi from '../services/sequences';
import * as templatesApi from '../services/queueTemplates';

/**
 * Feature 173 (FR-020, FR-022, FR-024): the big screen list and the phone show the same activity text, and the same
 * names for the same actions. One state of a queue goes to both screens, and the texts must be equal.
 */

jest.mock('../services/queues');
jest.mock('../services/sequences');
jest.mock('../services/queueTemplates');
jest.mock('../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [], loading: false, error: undefined, refresh: () => {} }),
}));

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;
const sequencesMock = sequencesApi as jest.Mocked<typeof sequencesApi>;
const templatesMock = templatesApi as jest.Mocked<typeof templatesApi>;

const at = (h: number, m: number) => new Date(2026, 9, 13, h, m, 0).toISOString();

const item = (over: Partial<queuesApi.QueueMonitorItemDto> = {}): queuesApi.QueueMonitorItemDto => ({
  sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleKind: 'OncePerRun', reason: '',
  expectedAt: null, relativeLabel: null, repeats: false, order: 0, ...over,
});

const monitor = (over: Partial<queuesApi.QueueMonitorDto> = {}): queuesApi.QueueMonitorDto => ({
  queueId: 'q1', name: 'Daily', running: true, cycleExecution: false, runStartedAt: null,
  current: null, upcoming: [], nothingScheduled: false, lastOutcome: null, ...over,
});

const queue = (over: Partial<queuesApi.QueueDto> = {}): queuesApi.QueueDto => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, pauseWhenIdle: false,
  idleThresholdSeconds: 30, status: 'Running', entryCount: 1, linkedTemplateId: 't1', linkedGameId: null, linkedGameName: null,
  ...over,
});

type State = { name: string; queue: Partial<queuesApi.QueueDto>; monitor: queuesApi.QueueMonitorDto; text: string };

const STATES: State[] = [
  { name: 'stopped', queue: { status: 'Stopped' }, monitor: monitor({ running: false }), text: 'Stopped' },
  { name: 'running a sequence', queue: { operatorPause: 'none' }, monitor: monitor({ current: item() }), text: 'Running: Help All' },
  { name: 'waiting for a timer', queue: { operatorPause: 'none' }, monitor: monitor({ upcoming: [item({ expectedAt: at(11, 5), scheduleKind: 'TimerTimeOfDay' })] }), text: 'Next run at 11:05' },
  { name: 'nothing scheduled', queue: { operatorPause: 'none' }, monitor: monitor({ nothingScheduled: true }), text: 'Nothing scheduled' },
  { name: 'pausing', queue: { operatorPause: 'pausing' }, monitor: monitor({ current: item() }), text: 'Pausing' },
  { name: 'paused', queue: { operatorPause: 'paused' }, monitor: monitor(), text: 'Paused' },
];

const health = (pause: 'none' | 'pausing' | 'paused'): queuesApi.QueueHealthDto => ({
  paused: pause === 'paused', pausedAt: null, pauseKind: pause === 'paused' ? 'operator' : null, pauseReason: null, operatorPause: pause,
});

const arrange = (state: State) => {
  const q = queue(state.queue);
  queuesMock.listQueues.mockResolvedValue([q]);
  queuesMock.getQueueMonitor.mockResolvedValue(state.monitor);
  queuesMock.getQueue.mockResolvedValue({
    ...q, linkedTemplateName: 'Daily tpl', entries: [], health: q.status === 'Running' ? health(q.operatorPause ?? 'none') : undefined,
  } as queuesApi.QueueDetailDto);
  return q;
};

beforeEach(() => {
  jest.resetAllMocks();
  sequencesMock.listSequences.mockResolvedValue([]);
  queuesMock.getQueueHealth.mockResolvedValue(null);
  templatesMock.getQueueTemplate.mockResolvedValue({ id: 't1', name: 'Daily tpl', entryCount: 0, createdAt: null, updatedAt: null, entries: [] });
});

const bigScreenText = async (expected: string): Promise<string> => {
  render(<QueuesPage />);
  const cell = await screen.findByTestId('queue-activity');
  await waitFor(() => expect(cell.textContent).toBe(expected));
  return cell.textContent ?? '';
};

const phoneText = async (expected: string): Promise<string> => {
  render(<PhoneQueuesView queueId={null} onOpenQueue={() => undefined} />);
  const card = await screen.findByTestId('queue-card-q1');
  const summary = card.querySelector('.phone-card__summary') as HTMLElement;
  await waitFor(() => expect((card.querySelector('.phone-card__summary') as HTMLElement).textContent).toBe(expected));
  return summary.textContent ?? '';
};

describe('Activity text parity (feature 173, US6)', () => {
  it.each(STATES)('shows the same text on both screens: $name', async (state) => {
    arrange(state);

    const big = await bigScreenText(state.text);
    document.body.innerHTML = '';
    const phone = await phoneText(state.text);

    expect(big).toBe(state.text);
    expect(phone).toBe(big);
  });

  it('covers the six states of FR-024', () => {
    expect(STATES.map((s) => s.name)).toEqual([
      'stopped', 'running a sequence', 'waiting for a timer', 'nothing scheduled', 'pausing', 'paused',
    ]);
  });

  it('shows the same text on both screens when the monitor call fails', async () => {
    const q = queue({ operatorPause: 'none' });
    queuesMock.listQueues.mockResolvedValue([q]);
    queuesMock.getQueueMonitor.mockRejectedValue(new Error('down'));

    const big = await bigScreenText('Running');
    document.body.innerHTML = '';
    const phone = await phoneText('Running');

    expect(phone).toBe(big);
  });
});

describe('Action names parity (feature 173, FR-022)', () => {
  const VERBS = ['Start', 'Stop', 'Pause', 'Resume', 'Cancel pause'];

  const bigScreenVerbs = async (): Promise<string[]> => {
    render(<QueuesPage />);
    const row = await screen.findByRole('row', { name: /Daily/ });
    return within(row).getAllByRole('button')
      .map((b) => (b.getAttribute('aria-label') ?? '').replace(/ Daily$/, ''))
      .filter((name) => VERBS.includes(name))
      .sort();
  };

  const phoneVerbs = async (): Promise<string[]> => {
    render(<PhoneQueuesView queueId="q1" onOpenQueue={() => undefined} />);
    await screen.findByTestId('phone-queue-detail');
    await screen.findByRole('heading', { name: 'Daily' });
    return screen.getAllByRole('button')
      .map((b) => b.textContent ?? '')
      .filter((name) => VERBS.includes(name))
      .sort();
  };

  it.each([
    ['a stopped queue', { status: 'Stopped' as const }, ['Start']],
    ['a running queue', { operatorPause: 'none' as const }, ['Pause', 'Stop']],
    ['a pausing queue', { operatorPause: 'pausing' as const }, ['Cancel pause', 'Stop']],
    ['a paused queue', { operatorPause: 'paused' as const }, ['Resume', 'Stop']],
  ])('uses the same action names on both screens for %s', async (_label, over, expected) => {
    const stateOf = (partial: Partial<queuesApi.QueueDto>): State => ({ name: 'x', queue: partial, monitor: monitor({ running: partial.status !== 'Stopped' }), text: '' });
    arrange(stateOf(over));

    const big = await bigScreenVerbs();
    document.body.innerHTML = '';
    const phone = await phoneVerbs();

    expect(big).toEqual([...expected].sort());
    expect(phone).toEqual(big);
  });
});
