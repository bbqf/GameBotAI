import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import {
  addQueueEntry,
  applyTemplate,
  getQueue,
  getQueueHealth,
  getTemplateDiff,
  listQueues,
  pauseQueue,
  removeQueueEntry,
  replaceQueueEntries,
  resumeQueue,
  updateQueue,
} from '../../services/queues';
import { listSequences } from '../../services/sequences';
import { getQueueTemplate, listQueueTemplates, saveQueueTemplate } from '../../services/queueTemplates';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/queueTemplates');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));
jest.mock('../../components/queues/TemplateUpgradeNotice', () => ({ TemplateUpgradeNotice: () => null }));
jest.mock('../../components/queues/QueueMonitor', () => ({
  QueueMonitor: ({ queueId, onReturnToEditor }: { queueId: string; onReturnToEditor?: () => void }) => (
    <div data-testid="queue-monitor-stub">
      monitor:{queueId}
      <button type="button" onClick={() => onReturnToEditor?.()}>Back to editor</button>
    </div>
  ),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const getQueueMock = getQueue as jest.MockedFunction<typeof getQueue>;
const updateQueueMock = updateQueue as jest.MockedFunction<typeof updateQueue>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const getTemplateMock = getQueueTemplate as jest.MockedFunction<typeof getQueueTemplate>;
const listTemplatesMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;
const saveTemplateMock = saveQueueTemplate as jest.MockedFunction<typeof saveQueueTemplate>;

const row = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Running', entryCount: 2, ...over,
});

const detail = (over: Partial<any> = {}) => ({
  ...row(over),
  pauseWhenIdle: false,
  idleThresholdSeconds: 30,
  linkedTemplateId: 't1',
  linkedTemplateName: 'Daily Farm',
  linkedGameId: null,
  linkedGameName: null,
  entries: [
    { entryId: 'r1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    { entryId: 'r2', sequenceId: 'seq-b', sequenceName: 'Bravo', stale: false },
  ],
  ...over,
});

const templateEntry = (id: string, sequenceId: string, name: string) => ({
  entryId: id, sequenceId, sequenceName: name, stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null,
  timerRelativeOffset: null, enabled: true,
});

const template = () => ({
  id: 't1', name: 'Daily Farm', entryCount: 2, createdAt: null, updatedAt: null,
  entries: [templateEntry('te1', 'seq-a', 'Alpha'), templateEntry('te2', 'seq-b', 'Bravo')],
});

let currentStatus: 'Running' | 'Stopped' = 'Running';

beforeEach(() => {
  jest.resetAllMocks();
  currentStatus = 'Running';
  listQueuesMock.mockImplementation(async () => [row({ status: currentStatus })] as any);
  getQueueMock.mockImplementation(async () => detail({ status: currentStatus }) as any);
  listSequencesMock.mockResolvedValue([{ id: 'seq-a', name: 'Alpha' }, { id: 'seq-b', name: 'Bravo' }] as any);
  getTemplateMock.mockResolvedValue(template() as any);
  listTemplatesMock.mockResolvedValue([{ id: 't1', name: 'Daily Farm', entryCount: 2, createdAt: null, updatedAt: null }] as any);
  saveTemplateMock.mockResolvedValue({ id: 't1', name: 'Daily Farm' } as any);
  updateQueueMock.mockResolvedValue({} as any);
});

const openRunningQueue = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
  await screen.findByTestId('queue-monitor-stub');
  await screen.findByRole('region', { name: 'Queue sequences' });
};

describe('Edit page of a running queue (feature 173, US1)', () => {
  it('enables Edit and the name link for a running queue', async () => {
    render(<QueuesPage />);
    await screen.findByText('Daily');

    expect(screen.getByRole('button', { name: /^Edit( Daily)?$/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Daily' })).toBeEnabled();
  });

  it('opens the page with the monitor and the template editor', async () => {
    render(<QueuesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByRole('button', { name: /^Edit( Daily)?$/ }));

    expect(await screen.findByTestId('queue-monitor-stub')).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Queue sequences' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Edit Queue' })).toBeInTheDocument();
    expect(getTemplateMock).toHaveBeenCalledWith('t1');
  });

  it('saves a template change without a queue entry call', async () => {
    await openRunningQueue();

    fireEvent.click(await screen.findByRole('button', { name: 'Remove Alpha' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

    await waitFor(() => expect(saveTemplateMock).toHaveBeenCalledTimes(1));
    expect(saveTemplateMock.mock.calls[0][0]).toEqual({
      name: 'Daily Farm',
      entries: [{ entryId: 'te2', sequenceId: 'seq-b', scheduleType: 'OncePerRun' }],
      overwrite: true,
    });
    expect(addQueueEntry).not.toHaveBeenCalled();
    expect(removeQueueEntry).not.toHaveBeenCalled();
    expect(replaceQueueEntries).not.toHaveBeenCalled();
  });

  it('saves the settings with updateQueue', async () => {
    await openRunningQueue();

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Daily 2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledTimes(1));
    expect(updateQueueMock.mock.calls[0][0]).toBe('q1');
    expect(updateQueueMock.mock.calls[0][1]).toMatchObject({ name: 'Daily 2', cycleExecution: false });
  });

  it('shows the emulator as read-only and the note near Save', async () => {
    await openRunningQueue();

    const emulator = screen.getByLabelText('Emulator *');
    expect(emulator).toHaveAttribute('readonly');
    expect(emulator).toBeDisabled();
    expect(screen.getByText('Settings apply at the next start.')).toBeInTheDocument();
  });

  it('does not show the settings note for a stopped queue', async () => {
    currentStatus = 'Stopped';
    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
    await screen.findByRole('heading', { name: 'Edit Queue' });

    expect(screen.queryByText('Settings apply at the next start.')).not.toBeInTheDocument();
    expect(screen.queryByTestId('queue-monitor-stub')).not.toBeInTheDocument();
  });

  it('keeps the page open when the queue stops', async () => {
    await openRunningQueue();
    currentStatus = 'Stopped';

    fireEvent.click(screen.getByRole('button', { name: 'Back to editor' }));

    await waitFor(() => expect(screen.queryByTestId('queue-monitor-stub')).not.toBeInTheDocument());
    expect(screen.getByRole('heading', { name: 'Edit Queue' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '← Back to list' })).toBeInTheDocument();
  });

  describe('pause controls and the notice of waiting changes (US5)', () => {
    const health = (operatorPause: 'none' | 'pausing' | 'paused') => ({
      paused: operatorPause === 'paused', pausedAt: null, pauseKind: operatorPause === 'paused' ? 'operator' : null,
      pauseReason: null, operatorPause,
    });
    const difference = { key: 'added:e9', type: 'added', entryId: 'e9', sequenceName: 'Charlie', summary: "Add the entry 'Charlie'.", selectedByDefault: true, affectsSchedule: false };
    let pauseState: 'none' | 'paused' = 'none';
    let waiting = 1;

    beforeEach(() => {
      pauseState = 'none';
      waiting = 1;
      (getQueueHealth as jest.Mock).mockImplementation(async () => health(pauseState));
      (getTemplateDiff as jest.Mock).mockImplementation(async () => ({
        queueId: 'q1', templateId: 't1', needsUpgrade: false, baseToken: 'tok', differences: waiting > 0 ? [difference] : [],
      }));
      (pauseQueue as jest.Mock).mockImplementation(async () => { pauseState = 'paused'; return { operatorPause: 'paused' }; });
      (resumeQueue as jest.Mock).mockImplementation(async () => { pauseState = 'none'; return { resumed: true }; });
      (applyTemplate as jest.Mock).mockImplementation(async () => { waiting = 0; return { applied: ['added:e9'], planVersion: 2 }; });
    });

    it('shows the pause controls and the notice above the monitor', async () => {
      await openRunningQueue();

      expect(await screen.findByTestId('queue-pause-controls')).toBeInTheDocument();
      expect(await screen.findByText(/1 change waits/)).toBeInTheDocument();
      const controls = screen.getByTestId('queue-pause-controls');
      const monitor = screen.getByTestId('queue-monitor-stub');
      expect(controls.compareDocumentPosition(monitor) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('shows no notice when the queue matches its template', async () => {
      waiting = 0;
      await openRunningQueue();
      await screen.findByTestId('queue-pause-controls');

      expect(screen.queryByTestId('template-changes-notice')).not.toBeInTheDocument();
    });

    it('lets the operator pause, review, apply, and resume', async () => {
      await openRunningQueue();
      fireEvent.click(await screen.findByRole('button', { name: 'Pause' }));
      await screen.findByText(/Paused \(between sequences\)/);

      fireEvent.click(await screen.findByRole('button', { name: 'Review changes' }));
      const apply = await screen.findByRole('button', { name: 'Apply 1 change' });
      await waitFor(() => expect(apply).toBeEnabled());
      fireEvent.click(apply);

      await waitFor(() => expect(applyTemplate).toHaveBeenCalledWith('q1', 'tok', ['added:e9']));
      expect(await screen.findByTestId('review-done')).toHaveTextContent('1 change applied');

      fireEvent.click(screen.getByRole('button', { name: 'Close' }));
      fireEvent.click(await screen.findByRole('button', { name: 'Resume' }));
      await waitFor(() => expect(resumeQueue).toHaveBeenCalledWith('q1'));
      await waitFor(() => expect(screen.queryByTestId('template-changes-notice')).not.toBeInTheDocument());
    });

    it('reads the waiting changes again after a template save', async () => {
      await openRunningQueue();
      await screen.findByText(/1 change waits/);
      const calls = (getTemplateDiff as jest.Mock).mock.calls.length;

      fireEvent.click(await screen.findByRole('button', { name: 'Remove Alpha' }));
      fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

      await waitFor(() => expect((getTemplateDiff as jest.Mock).mock.calls.length).toBeGreaterThan(calls));
    });
  });

  describe('enable and disable of entries (US7, FR-026)', () => {
    it('shows an entry that the template turns off as disabled in the running editor', async () => {
      const tpl = template();
      tpl.entries[0].enabled = false;
      getTemplateMock.mockResolvedValue(tpl as any);

      await openRunningQueue();

      expect(await screen.findByRole('switch', { name: 'Enable Alpha' })).not.toBeChecked();
      expect(screen.getByRole('switch', { name: 'Disable Bravo' })).toBeChecked();
    });

    it('saves an entry that the operator turns off, and shows it as disabled after a refresh', async () => {
      await openRunningQueue();

      fireEvent.click(await screen.findByRole('switch', { name: 'Disable Bravo' }));
      fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

      await waitFor(() => expect(saveTemplateMock).toHaveBeenCalledTimes(1));
      expect(saveTemplateMock.mock.calls[0][0].entries).toEqual([
        { entryId: 'te1', sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
        { entryId: 'te2', sequenceId: 'seq-b', scheduleType: 'OncePerRun', enabled: false },
      ]);
      expect(addQueueEntry).not.toHaveBeenCalled();

      // After a refresh the template says the entry is off.
      const tpl = template();
      tpl.entries[1].enabled = false;
      getTemplateMock.mockResolvedValue(tpl as any);
      document.body.innerHTML = '';
      await openRunningQueue();
      expect(await screen.findByRole('switch', { name: 'Enable Bravo' })).not.toBeChecked();
    });
  });

  describe('when the queue starts during an edit', () => {
    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    it('shows the live monitor and keeps the unsaved edits', async () => {
      currentStatus = 'Stopped';
      render(<QueuesPage />);
      await screen.findByText('Daily');
      fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
      await screen.findByRole('heading', { name: 'Edit Queue' });
      fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Unsaved name' } });

      currentStatus = 'Running';
      await act(async () => { jest.advanceTimersByTime(5000); });

      expect(await screen.findByTestId('queue-monitor-stub')).toBeInTheDocument();
      expect(screen.getByLabelText('Name *')).toHaveValue('Unsaved name');
      const sequences = within(await screen.findByRole('region', { name: 'Queue sequences' }));
      expect(sequences.getByRole('button', { name: 'Remove Alpha' })).toBeInTheDocument();
    });
  });
});
