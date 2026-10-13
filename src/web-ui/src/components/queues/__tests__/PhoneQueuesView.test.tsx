import React, { useState } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { PhoneQueuesView, PhoneQueuesViewProps } from '../PhoneQueuesView';
import { ApiError } from '../../../lib/api';
import * as queuesApi from '../../../services/queues';
import * as templatesApi from '../../../services/queueTemplates';

jest.mock('../../../services/queues');
jest.mock('../../../services/queueTemplates');

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;
const templatesMock = templatesApi as jest.Mocked<typeof templatesApi>;

/** Holds the open queue id like App.tsx does, so the view gets it as a prop. */
const View: React.FC<PhoneQueuesViewProps & { initialQueueId?: string | null }> = ({ initialQueueId = null, ...props }) => {
  const [queueId, setQueueId] = useState<string | null>(initialQueueId);
  return <PhoneQueuesView {...props} queueId={queueId} onOpenQueue={setQueueId} />;
};

const monitorDto = (over: Partial<queuesApi.QueueMonitorDto> = {}): queuesApi.QueueMonitorDto => ({
  queueId: 'q1', name: 'Daily', running: false, cycleExecution: false, runStartedAt: null,
  current: null, upcoming: [], nothingScheduled: false, lastOutcome: null, ...over
});

const LONG_NAME = 'Very long queue name '.repeat(10).trim();

const queue = (over: Partial<queuesApi.QueueDto> = {}): queuesApi.QueueDto => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emulator-5558', cycleExecution: false, pauseWhenIdle: false,
  idleThresholdSeconds: 30, status: 'Stopped', entryCount: 2, linkedTemplateId: 't1', linkedGameId: null, linkedGameName: null,
  ...over
});

const detail = (over: Partial<queuesApi.QueueDetailDto> = {}): queuesApi.QueueDetailDto => ({
  ...queue(),
  linkedTemplateName: 'Daily tpl',
  entries: [
    { entryId: 'e1', sequenceId: 's1', sequenceName: 'Help All', stale: false },
    { entryId: 'e2', sequenceId: 's2', sequenceName: 'Gift', stale: false }
  ],
  ...over
});

const template = (): templatesApi.QueueTemplateDetail => ({
  id: 't1', name: 'Daily tpl', entryCount: 2, createdAt: null, updatedAt: null,
  entries: [
    { sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true },
    { sequenceId: 's2', sequenceName: 'Gift', stale: false, scheduleType: 'Timer', timerTimeOfDay: '04:00:00', timerRelativeOffset: null, enabled: false }
  ]
});

beforeEach(() => {
  jest.resetAllMocks();
  queuesMock.listQueues.mockResolvedValue([queue(), queue({ id: 'q2', name: LONG_NAME, status: 'Running' })]);
  queuesMock.getQueue.mockResolvedValue(detail());
  queuesMock.getQueueMonitor.mockResolvedValue(monitorDto());
  templatesMock.getQueueTemplate.mockResolvedValue(template());
});

describe('PhoneQueuesView list', () => {
  it('shows name, status chip, and one Start or Stop button for each queue', async () => {
    render(<View />);
    const card = await screen.findByTestId('queue-card-q1');
    expect(within(card).getByText('Daily')).toBeInTheDocument();
    expect(within(card).getByText('Stopped', { selector: '[data-testid="queue-status"] span' })).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Start' })).toBeInTheDocument();
    const running = screen.getByTestId('queue-card-q2');
    expect(within(running).getByRole('button', { name: 'Stop' })).toBeInTheDocument();
  });

  it('ends a long name with an ellipsis and keeps the full name in the detail', async () => {
    queuesMock.getQueue.mockResolvedValue(detail({ id: 'q2', name: LONG_NAME }));
    render(<View />);
    const title = (await screen.findAllByText(LONG_NAME))[0];
    expect(title).toHaveClass('phone-card__title');
    expect(title).toHaveAttribute('title', LONG_NAME);
    fireEvent.click(screen.getByRole('button', { name: `Open ${LONG_NAME}` }));
    expect(await screen.findByRole('heading', { name: LONG_NAME })).toBeInTheDocument();
  });

  it('starts a queue', async () => {
    queuesMock.startQueue.mockResolvedValue(queue({ status: 'Running' }));
    render(<View />);
    fireEvent.click(await screen.findByRole('button', { name: 'Start' }));
    await waitFor(() => expect(queuesMock.startQueue).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(within(screen.getByTestId('queue-card-q1')).getByRole('button', { name: 'Stop' })).toBeInTheDocument());
  });

  it('stops a queue', async () => {
    queuesMock.stopQueue.mockResolvedValue(queue({ id: 'q2', name: LONG_NAME, status: 'Stopped' }));
    render(<View />);
    const card = await screen.findByTestId('queue-card-q2');
    fireEvent.click(within(card).getByRole('button', { name: 'Stop' }));
    await waitFor(() => expect(queuesMock.stopQueue).toHaveBeenCalledWith('q2'));
  });

  it('disables Start and Stop when the connection is lost', async () => {
    render(<View connectionState="lost" />);
    const card = await screen.findByTestId('queue-card-q1');
    expect(within(card).getByRole('button', { name: 'Start' })).toBeDisabled();
    expect(within(screen.getByTestId('queue-card-q2')).getByRole('button', { name: 'Stop' })).toBeDisabled();
  });

  it('shows no create, edit, duplicate, delete, schedule, or reorder control', async () => {
    render(<View />);
    await screen.findByTestId('queue-card-q1');
    expect(screen.queryByRole('button', { name: /create|new queue|edit|duplicate|delete|schedule|reorder|move/i })).toBeNull();
  });

  it('reloads the list when the reload signal changes', async () => {
    const { rerender } = render(<View reloadSignal={0} />);
    await screen.findByTestId('queue-card-q1');
    const calls = queuesMock.listQueues.mock.calls.length;
    rerender(<View reloadSignal={1} />);
    await waitFor(() => expect(queuesMock.listQueues.mock.calls.length).toBe(calls + 1));
  });
});

describe('PhoneQueuesView detail', () => {
  const openFirst = async () => {
    render(<View />);
    fireEvent.click(await screen.findByRole('button', { name: 'Open Daily' }));
    await screen.findByTestId('phone-queue-detail');
  };

  it('opens the detail with entry switches and the hints', async () => {
    await openFirst();
    expect(await screen.findByRole('switch', { name: 'Help All' })).toBeChecked();
    expect(screen.getByRole('switch', { name: 'Gift' })).not.toBeChecked();
    expect(screen.getByText('Changes apply when the queue starts again.')).toBeInTheDocument();
    expect(screen.getByText('Use a larger screen to edit schedules.')).toBeInTheDocument();
  });

  it('saves the linked template when a switch changes', async () => {
    const saved = template();
    saved.entries[0].enabled = false;
    templatesMock.saveQueueTemplate.mockResolvedValue(saved);
    await openFirst();
    fireEvent.click(await screen.findByRole('switch', { name: 'Help All' }));
    await waitFor(() => expect(templatesMock.saveQueueTemplate).toHaveBeenCalledTimes(1));
    const request = templatesMock.saveQueueTemplate.mock.calls[0][0];
    expect(request.name).toBe('Daily tpl');
    expect(request.overwrite).toBe(true);
    expect(request.entries.map((e) => e.enabled)).toEqual([false, false]);
    expect(request.entries[1]).toMatchObject({ scheduleType: 'Timer', timerTimeOfDay: '04:00:00' });
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Help All' })).not.toBeChecked());
  });

  it('shows read-only switches when no template is linked', async () => {
    queuesMock.getQueue.mockResolvedValue(detail({ linkedTemplateId: null, linkedTemplateName: null }));
    await openFirst();
    const sw = await screen.findByRole('switch', { name: 'Help All' });
    expect(sw).toBeDisabled();
    expect(screen.getByText(/No template is linked/)).toBeInTheDocument();
    expect(screen.queryByText('Changes apply when the queue starts again.')).toBeNull();
  });

  it('disables the switches and Start when the connection is lost', async () => {
    const view = render(<View connectionState="lost" />);
    fireEvent.click(await view.findByRole('button', { name: 'Open Daily' }));
    expect(await view.findByRole('switch', { name: 'Help All' })).toBeDisabled();
    expect(view.getAllByRole('button', { name: 'Start' })[0]).toBeDisabled();
  });

  it('goes back to the list', async () => {
    window.history.replaceState({ idx: 0 }, '', '/');
    await openFirst();
    fireEvent.click(screen.getByRole('button', { name: /Back/ }));
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
  });
});

describe('PhoneQueuesView activity', () => {
  const at = (h: number, m: number) => new Date(2026, 9, 13, h, m, 0).toISOString();
  const openFirst = async () => {
    render(<View />);
    fireEvent.click(await screen.findByRole('button', { name: 'Open Daily' }));
    await screen.findByTestId('phone-queue-detail');
  };
  const runningMonitor = () => monitorDto({
    running: true,
    current: {
      sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleKind: 'OncePerRun', reason: '',
      expectedAt: null, relativeLabel: 'now', repeats: false, order: 0
    },
    upcoming: [{
      sequenceId: 's2', sequenceName: 'Gift', stale: false, scheduleKind: 'TimerTimeOfDay', reason: '',
      expectedAt: at(23, 30), relativeLabel: null, repeats: true, order: 1
    }],
    lastOutcome: { status: 'Succeeded', summary: 'All steps done' }
  });

  it('shows the state, the Now text, the last outcome, and the schedule rows in order', async () => {
    queuesMock.getQueue.mockResolvedValue(detail({ status: 'Running' }));
    queuesMock.getQueueMonitor.mockResolvedValue(runningMonitor());
    const tpl = template();
    tpl.entries[1].enabled = true;
    templatesMock.getQueueTemplate.mockResolvedValue(tpl);
    render(<View initialQueueId="q1" />);
    await waitFor(() => expect(screen.getByTestId('queue-now')).toHaveTextContent('Running: Help All'));
    expect(screen.getByTestId('queue-status')).toHaveTextContent('Running');
    expect(screen.getByTestId('queue-last-outcome')).toHaveTextContent('Succeeded - All steps done');
    const list = screen.getByRole('list', { name: 'Queue schedule' });
    const names = within(list).getAllByRole('switch').map((el) => el.getAttribute('aria-label'));
    expect(names).toEqual(['Gift', 'Help All']);
    expect(within(list).getByText(/23:30/)).toBeInTheDocument();
  });

  it('marks disabled rows and shows no false running data for a stopped queue', async () => {
    render(<View initialQueueId="q1" />);
    await waitFor(() => expect(screen.getByTestId('queue-now')).toHaveTextContent('Stopped'));
    const list = screen.getByRole('list', { name: 'Queue schedule' });
    expect(within(list).getByText('disabled')).toBeInTheDocument();
    expect(within(list).queryByText(/in \d+ min/)).toBeNull();
  });

  it('shows a one-line summary on the card', async () => {
    queuesMock.getQueueMonitor.mockResolvedValue(runningMonitor());
    render(<View />);
    const running = await screen.findByTestId('queue-card-q2');
    await waitFor(() => expect(running).toHaveTextContent('Running: Help All'));
    expect(screen.getByTestId('queue-card-q1')).toHaveTextContent('Stopped');
  });

  it('shows an empty-state text when the queue has no entries', async () => {
    queuesMock.getQueue.mockResolvedValue(detail({ entries: [], linkedTemplateId: null }));
    render(<View initialQueueId="q1" />);
    expect(await screen.findByText('This queue has no entries.')).toBeInTheDocument();
  });

  it('gives a long entry name a title attribute', async () => {
    const tpl = template();
    tpl.entries[0].sequenceName = LONG_NAME;
    templatesMock.getQueueTemplate.mockResolvedValue(tpl);
    render(<View initialQueueId="q1" />);
    const title = await screen.findByText(LONG_NAME);
    expect(title).toHaveAttribute('title', LONG_NAME);
  });

  it('shows no control for schedule creation, reorder, or edit', async () => {
    await openFirst();
    await screen.findByRole('switch', { name: 'Help All' });
    expect(screen.queryByRole('button', { name: /add|edit|delete|reorder|move|timer|schedule/i })).toBeNull();
    const names = screen.getAllByRole('button').map((b) => b.textContent);
    expect(names).toEqual(['← Back', 'Start']);
  });

  it('shows an old-data mark when a refresh fails and clears it on the next good refresh', async () => {
    jest.useFakeTimers();
    try {
      render(<View initialQueueId="q1" />);
      await act(async () => { await Promise.resolve(); });
      expect(await screen.findByRole('heading', { name: 'Daily' })).toBeInTheDocument();
      expect(screen.queryByTestId('stale-mark')).toBeNull();
      queuesMock.getQueueMonitor.mockRejectedValue(new Error('down'));
      await act(async () => { jest.advanceTimersByTime(5000); });
      expect(screen.getByTestId('stale-mark')).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Daily' })).toBeInTheDocument();
      queuesMock.getQueueMonitor.mockResolvedValue(monitorDto());
      await act(async () => { jest.advanceTimersByTime(5000); });
      expect(screen.queryByTestId('stale-mark')).toBeNull();
    } finally {
      jest.useRealTimers();
    }
  });

  it('keeps the card data and marks it old when the list refresh fails', async () => {
    jest.useFakeTimers();
    try {
      render(<View />);
      await act(async () => { await Promise.resolve(); });
      expect(await screen.findByTestId('queue-card-q1')).toBeInTheDocument();
      queuesMock.listQueues.mockRejectedValue(new Error('down'));
      await act(async () => { jest.advanceTimersByTime(5000); });
      expect(screen.getByTestId('queue-card-q1')).toBeInTheDocument();
      expect(screen.getByTestId('stale-mark')).toBeInTheDocument();
      queuesMock.listQueues.mockResolvedValue([queue()]);
      await act(async () => { jest.advanceTimersByTime(5000); });
      expect(screen.queryByTestId('stale-mark')).toBeNull();
    } finally {
      jest.useRealTimers();
    }
  });

  it('disables Start, Stop, and the switches while the connection is lost', async () => {
    render(<View connectionState="lost" initialQueueId="q1" />);
    expect(await screen.findByRole('switch', { name: 'Help All' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled();
  });
});

describe('PhoneQueuesView polling (SC-002)', () => {
  const setVisibility = (state: 'visible' | 'hidden') => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
    document.dispatchEvent(new Event('visibilitychange'));
  };

  afterEach(() => {
    jest.useRealTimers();
    setVisibility('visible');
  });

  it('shows new data within 5 s and sends no request while hidden', async () => {
    jest.useFakeTimers();
    render(<View initialQueueId="q1" />);
    await act(async () => { await Promise.resolve(); });
    await waitFor(() => expect(screen.getByTestId('queue-now')).toHaveTextContent('Stopped'));

    queuesMock.getQueue.mockResolvedValue(detail({ status: 'Running' }));
    queuesMock.getQueueMonitor.mockResolvedValue(monitorDto({
      running: true,
      current: {
        sequenceId: 's1', sequenceName: 'Fresh', stale: false, scheduleKind: 'OncePerRun', reason: '',
        expectedAt: null, relativeLabel: null, repeats: false, order: 0
      }
    }));
    await act(async () => { jest.advanceTimersByTime(5000); });
    expect(screen.getByTestId('queue-now')).toHaveTextContent('Running: Fresh');

    act(() => setVisibility('hidden'));
    const calls = queuesMock.getQueue.mock.calls.length;
    await act(async () => { jest.advanceTimersByTime(30000); });
    expect(queuesMock.getQueue.mock.calls.length).toBe(calls);
    await act(async () => { setVisibility('visible'); });
    expect(queuesMock.getQueue.mock.calls.length).toBe(calls + 1);
  });

  it('sends no request after the connection is lost', async () => {
    jest.useFakeTimers();
    const { rerender } = render(<View initialQueueId="q1" connectionState="online" />);
    await act(async () => { await Promise.resolve(); });
    rerender(<View initialQueueId="q1" connectionState="lost" />);
    const calls = queuesMock.getQueue.mock.calls.length;
    await act(async () => { jest.advanceTimersByTime(30000); });
    expect(queuesMock.getQueue.mock.calls.length).toBe(calls);
  });
});

describe('PhoneQueuesView queue id', () => {
  it('shows "Queue not found" with a button to the list for an unknown id', async () => {
    queuesMock.getQueue.mockRejectedValue(new ApiError(404, 'Not found'));
    render(<View initialQueueId="nope" />);
    expect(await screen.findByText('Queue not found')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Go to the queue list' }));
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
  });

  it('takes the open queue from the prop, not from local state', async () => {
    const onOpenQueue = jest.fn();
    render(<PhoneQueuesView queueId={null} onOpenQueue={onOpenQueue} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Open Daily' }));
    expect(onOpenQueue).toHaveBeenCalledWith('q1');
    expect(screen.queryByTestId('phone-queue-detail')).toBeNull();
  });
});
