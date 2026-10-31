import React from 'react';
import { act, render, screen, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { getQueueMonitor, listQueues } from '../../services/queues';
import { listSequences } from '../../services/sequences';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../components/queues/QueuePauseControls', () => ({ QueuePauseControls: () => null }));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const monitorMock = getQueueMonitor as jest.MockedFunction<typeof getQueueMonitor>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Running', entryCount: 1, ...over,
});

const item = (over: Partial<any> = {}) => ({
  sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleKind: 'OncePerRun', reason: '',
  expectedAt: null, relativeLabel: null, repeats: false, order: 0, ...over,
});

const snapshot = (id: string, over: Partial<any> = {}) => ({
  queueId: id, name: id, running: true, cycleExecution: false, runStartedAt: null,
  current: null, upcoming: [], nothingScheduled: false, lastOutcome: null, ...over,
});

const at = (h: number, m: number) => new Date(2026, 9, 13, h, m, 0).toISOString();

const activityOf = (name: RegExp) => within(screen.getByRole('row', { name })).getByTestId('queue-activity');

beforeEach(() => {
  jest.resetAllMocks();
  listSequencesMock.mockResolvedValue([] as any);
});

const renderList = async (queues: any[]) => {
  listQueuesMock.mockResolvedValue(queues as any);
  render(<QueuesPage />);
  await screen.findByText(queues[0].name);
};

describe('Queues list Activity column (feature 173, US2)', () => {
  it('has the columns Name, Cycle, Status, Activity, and Actions, and no Emulator column', async () => {
    monitorMock.mockResolvedValue(snapshot('q1') as any);
    await renderList([queue()]);

    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers).toEqual(['Name', 'Cycle', 'Status', 'Activity', 'Actions']);
    expect(screen.queryByText('emu-1')).not.toBeInTheDocument();
  });

  it('shows the running sequence, the next run time, and Stopped', async () => {
    monitorMock.mockImplementation(async (id: string) => (id === 'run' ? snapshot(id, { current: item() }) : snapshot(id, { upcoming: [item({ expectedAt: at(11, 5) })] })) as any);
    await renderList([
      queue({ id: 'run', name: 'Active' }),
      queue({ id: 'wait', name: 'Waiting one' }),
      queue({ id: 'off', name: 'Idle one', status: 'Stopped' }),
    ]);

    expect(await within(screen.getByRole('row', { name: /Active/ })).findByText('Running: Help All')).toBeInTheDocument();
    expect(activityOf(/Waiting one/)).toHaveTextContent('Next run at 11:05');
    expect(activityOf(/Idle one/)).toHaveTextContent('Stopped');
  });

  it('shows Paused and Pausing from the pause state of the list', async () => {
    monitorMock.mockImplementation(async (id: string) => snapshot(id, { current: item() }) as any);
    await renderList([
      queue({ id: 'a', name: 'Paused one', operatorPause: 'paused' }),
      queue({ id: 'b', name: 'Pausing one', operatorPause: 'pausing' }),
    ]);

    expect(activityOf(/Paused one/)).toHaveTextContent(/^Paused$/);
    expect(activityOf(/Pausing one/)).toHaveTextContent(/^Pausing$/);
  });

  it('asks for the monitor of a running queue only', async () => {
    monitorMock.mockResolvedValue(snapshot('run') as any);
    await renderList([queue({ id: 'run', name: 'Active' }), queue({ id: 'off', name: 'Idle one', status: 'Stopped' })]);
    await screen.findByText('Waiting');

    expect(monitorMock).toHaveBeenCalledWith('run');
    expect(monitorMock).not.toHaveBeenCalledWith('off');
  });

  it('keeps the row and shows the status only when the monitor call fails', async () => {
    monitorMock.mockImplementation(async (id: string) => {
      if (id === 'bad') throw new Error('monitor down');
      return snapshot(id, { current: item() }) as any;
    });
    await renderList([queue({ id: 'bad', name: 'Broken' }), queue({ id: 'good', name: 'Healthy' })]);

    expect(await within(screen.getByRole('row', { name: /Healthy/ })).findByText('Running: Help All')).toBeInTheDocument();
    expect(activityOf(/Broken/)).toHaveTextContent(/^Running$/);
    expect(screen.getByRole('row', { name: /Broken/ })).toBeInTheDocument();
  });

  describe('after a poll', () => {
    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    it('updates the text from the new monitor data and the new pause state', async () => {
      monitorMock.mockResolvedValue(snapshot('q1', { current: item() }) as any);
      listQueuesMock.mockResolvedValue([queue()] as any);
      render(<QueuesPage />);
      expect(await within(await screen.findByRole('row', { name: /Daily/ })).findByText('Running: Help All')).toBeInTheDocument();

      monitorMock.mockResolvedValue(snapshot('q1', { nothingScheduled: true }) as any);
      await act(async () => { jest.advanceTimersByTime(5000); });
      expect(activityOf(/Daily/)).toHaveTextContent('Nothing scheduled');

      listQueuesMock.mockResolvedValue([queue({ operatorPause: 'paused' })] as any);
      await act(async () => { jest.advanceTimersByTime(5000); });
      expect(activityOf(/Daily/)).toHaveTextContent(/^Paused$/);
    });

    it('does not poll when no queue runs', async () => {
      listQueuesMock.mockResolvedValue([queue({ status: 'Stopped' })] as any);
      render(<QueuesPage />);
      await screen.findByText('Daily');
      const calls = listQueuesMock.mock.calls.length;

      await act(async () => { jest.advanceTimersByTime(15000); });

      expect(listQueuesMock.mock.calls.length).toBe(calls);
      expect(monitorMock).not.toHaveBeenCalled();
    });
  });
});
