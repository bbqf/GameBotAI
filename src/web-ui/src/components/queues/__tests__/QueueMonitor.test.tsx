import React from 'react';
import { act, render, screen } from '@testing-library/react';
import { QueueMonitor } from '../QueueMonitor';
import { getQueueMonitor, QueueMonitorDto } from '../../../services/queues';

jest.mock('../../../services/queues');

const getQueueMonitorMock = getQueueMonitor as jest.MockedFunction<typeof getQueueMonitor>;

const item = (over: Partial<QueueMonitorDto['upcoming'][number]> = {}): QueueMonitorDto['upcoming'][number] => ({
  sequenceId: 's', sequenceName: 'Seq', stale: false, scheduleKind: 'OncePerRun',
  reason: 'Once per run', expectedAt: null, relativeLabel: null, repeats: false, order: 0, ...over,
});

const snapshot = (over: Partial<QueueMonitorDto> = {}): QueueMonitorDto => ({
  queueId: 'q1', name: 'Daily', running: true, cycleExecution: false, runStartedAt: '2026-01-01T12:00:00+00:00',
  current: null, upcoming: [], nothingScheduled: false, lastOutcome: null, ...over,
});

const flush = async () => {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
};

describe('QueueMonitor', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.resetAllMocks();
  });
  afterEach(() => {
    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });

  it('renders the Now row and the ordered Up next list', async () => {
    getQueueMonitorMock.mockResolvedValue(snapshot({
      current: item({ sequenceId: 'A', sequenceName: 'Alpha', relativeLabel: 'now' }),
      upcoming: [
        item({ sequenceId: 'B', sequenceName: 'Bravo', relativeLabel: 'next', order: 0 }),
        item({ sequenceId: 'C', sequenceName: 'Charlie', relativeLabel: 'up next', order: 1 }),
      ],
    }));

    render(<QueueMonitor queueId="q1" />);
    await flush();

    expect(screen.getByTestId('monitor-now')).toHaveTextContent('Alpha');
    const rows = screen.getAllByTestId('monitor-upcoming-item');
    expect(rows.map((r) => r.textContent)).toEqual([
      expect.stringContaining('Bravo'),
      expect.stringContaining('Charlie'),
    ]);
  });

  it('re-polls on the ~2.5s interval and updates the list', async () => {
    getQueueMonitorMock.mockResolvedValueOnce(snapshot({ upcoming: [item({ sequenceId: 'B', sequenceName: 'Bravo' })] }));
    getQueueMonitorMock.mockResolvedValueOnce(snapshot({ upcoming: [item({ sequenceId: 'C', sequenceName: 'Charlie' })] }));

    render(<QueueMonitor queueId="q1" />);
    await flush();
    expect(screen.getByText('Bravo')).toBeInTheDocument();
    expect(getQueueMonitorMock).toHaveBeenCalledTimes(1);

    await act(async () => { jest.advanceTimersByTime(2500); });
    await flush();

    expect(getQueueMonitorMock).toHaveBeenCalledTimes(2);
    expect(screen.getByText('Charlie')).toBeInTheDocument();
  });

  // ── US3: idle / empty / ended states ─────────────────────────────────────

  it('renders the running & waiting-until state for an idle-but-alive queue', async () => {
    getQueueMonitorMock.mockResolvedValue(snapshot({
      current: null,
      upcoming: [item({ sequenceId: 'T', sequenceName: 'Timer', scheduleKind: 'TimerTimeOfDay', reason: 'At 15:00', expectedAt: '2026-01-01T15:00:00+00:00', relativeLabel: 'waiting' })],
    }));

    render(<QueueMonitor queueId="q1" />);
    await flush();

    expect(screen.getByTestId('monitor-now')).toHaveTextContent(/waiting until/i);
  });

  it('renders the nothing-scheduled empty state', async () => {
    getQueueMonitorMock.mockResolvedValue(snapshot({ nothingScheduled: true, upcoming: [] }));

    render(<QueueMonitor queueId="q1" />);
    await flush();

    expect(screen.getByTestId('monitor-nothing')).toBeInTheDocument();
  });

  it('renders the ended state with the last outcome and stops polling', async () => {
    getQueueMonitorMock.mockResolvedValue(snapshot({
      running: false, current: null,
      lastOutcome: { status: 'success', summary: 'Queue completed full run: 3 sequence(s) executed.' },
    }));

    render(<QueueMonitor queueId="q1" />);
    await flush();

    const ended = screen.getByTestId('monitor-ended');
    expect(ended).toHaveTextContent(/Run ended/i);
    expect(ended).toHaveTextContent(/completed full run/i);

    // Polling stops once the run has ended: further ticks do not re-fetch.
    const callsAfterEnd = getQueueMonitorMock.mock.calls.length;
    await act(async () => { jest.advanceTimersByTime(7500); });
    await flush();
    expect(getQueueMonitorMock).toHaveBeenCalledTimes(callsAfterEnd);
  });
});
