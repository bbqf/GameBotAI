import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { PhoneQueuesView, PhoneQueuesViewProps, UPGRADE_HINT } from '../PhoneQueuesView';
import * as queuesApi from '../../../services/queues';
import * as templatesApi from '../../../services/queueTemplates';
import * as sequencesApi from '../../../services/sequences';

jest.mock('../../../services/queues');
jest.mock('../../../services/queueTemplates');
jest.mock('../../../services/sequences');

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;
const templatesMock = templatesApi as jest.Mocked<typeof templatesApi>;
const sequencesMock = sequencesApi as jest.Mocked<typeof sequencesApi>;

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

const template = (withIds = true, needsUpgrade = false): templatesApi.QueueTemplateDetail => ({
  id: 't1', name: 'Daily tpl', entryCount: 2, createdAt: null, updatedAt: null, needsUpgrade,
  entries: [
    { ...(withIds ? { entryId: 'e1' } : {}), sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true },
    { ...(withIds ? { entryId: 'e2' } : {}), sequenceId: 's2', sequenceName: 'Gift', stale: false, scheduleType: 'Timer', timerTimeOfDay: '04:00:00', timerRelativeOffset: null, enabled: true }
  ]
});

const renderDetail = (props: Partial<PhoneQueuesViewProps> = {}) =>
  render(<PhoneQueuesView queueId="q1" onOpenQueue={jest.fn()} {...props} />);

beforeEach(() => {
  jest.resetAllMocks();
  queuesMock.listQueues.mockResolvedValue([queue()]);
  queuesMock.getQueue.mockResolvedValue(detail());
  queuesMock.getQueueMonitor.mockResolvedValue({
    queueId: 'q1', name: 'Daily', running: false, cycleExecution: false, runStartedAt: null,
    current: null, upcoming: [], nothingScheduled: false, lastOutcome: null
  });
  templatesMock.getQueueTemplate.mockResolvedValue(template());
  sequencesMock.getSequence.mockResolvedValue({ id: 's1', name: 'Help All', steps: [], parameters: [] } as unknown as sequencesApi.SequenceDto);
});

describe('PhoneQueuesView rows that open the entry page (feature 185, rules P-01 to P-05)', () => {
  it('opens the entry page for a tap on a row with an entry ID', async () => {
    const onOpenEntry = jest.fn();
    renderDetail({ onOpenEntry });

    fireEvent.click(await screen.findByRole('button', { name: 'Entry settings for Help All' }));

    expect(onOpenEntry).toHaveBeenCalledWith('q1', 'e1');
  });

  it('does not open the page for a tap on the enable switch', async () => {
    const onOpenEntry = jest.fn();
    templatesMock.saveQueueTemplate.mockResolvedValue(template());
    renderDetail({ onOpenEntry });
    await screen.findByRole('button', { name: 'Entry settings for Help All' });

    fireEvent.click(screen.getByRole('switch', { name: 'Help All' }));

    expect(onOpenEntry).not.toHaveBeenCalled();
    await waitFor(() => expect(templatesMock.saveQueueTemplate).toHaveBeenCalledTimes(1));
  });

  it('does not open the page for a tap on Run next', async () => {
    const onOpenEntry = jest.fn();
    queuesMock.getQueue.mockResolvedValue(detail({ status: 'Running' }));
    queuesMock.getQueueMonitor.mockResolvedValue({
      queueId: 'q1', name: 'Daily', running: true, cycleExecution: false, runStartedAt: null,
      current: null, upcoming: [], nothingScheduled: false, lastOutcome: null,
      entryStates: { e1: 'idle', e2: 'idle' }
    } as queuesApi.QueueMonitorDto);
    queuesMock.runNextEntry.mockResolvedValue(undefined as never);
    renderDetail({ onOpenEntry });

    fireEvent.click(await screen.findByRole('button', { name: 'Run next Help All' }));

    expect(onOpenEntry).not.toHaveBeenCalled();
  });

  it('gives the row button a hit area class and a name', async () => {
    renderDetail({ onOpenEntry: jest.fn() });

    const button = await screen.findByRole('button', { name: 'Entry settings for Help All' });
    expect(button).toHaveClass('phone-card__body', 'phone-schedule-row__open');
    expect(button.tagName).toBe('BUTTON');
  });

  it('does not make a row without an entry ID tappable, and shows the upgrade hint for a template that needs an upgrade', async () => {
    templatesMock.getQueueTemplate.mockResolvedValue(template(false, true));
    renderDetail({ onOpenEntry: jest.fn() });

    await screen.findByText('Help All');
    expect(screen.queryByRole('button', { name: /Entry settings for/ })).toBeNull();
    expect(screen.getAllByText(UPGRADE_HINT)).toHaveLength(2);
  });

  it('shows no upgrade hint for a template that has all entry IDs', async () => {
    renderDetail({ onOpenEntry: jest.fn() });

    await screen.findByRole('button', { name: 'Entry settings for Help All' });
    expect(screen.queryByText(UPGRADE_HINT)).toBeNull();
  });

  it('shows no extra hint for a queue with no linked template, and the rows are not tappable', async () => {
    queuesMock.getQueue.mockResolvedValue(detail({ linkedTemplateId: null, linkedTemplateName: null }));
    renderDetail({ onOpenEntry: jest.fn() });

    await screen.findByText('Help All');
    expect(screen.queryByRole('button', { name: /Entry settings for/ })).toBeNull();
    expect(screen.queryByText(UPGRADE_HINT)).toBeNull();
    expect(screen.getByText('No template is linked to this queue. The switches are read-only.')).toBeInTheDocument();
  });

  it('keeps the row text and the larger-screen text', async () => {
    renderDetail({ onOpenEntry: jest.fn() });

    const list = await screen.findByRole('list', { name: 'Queue schedule' });
    expect(within(list).getByText('Once each run')).toBeInTheDocument();
    expect(within(list).getByText('Daily at 04:00')).toBeInTheDocument();
    expect(screen.getByText('Use a larger screen to edit schedules.')).toBeInTheDocument();
  });

  it('shows the entry page for a set entry ID, and mounts no inline settings panel (FR-021)', async () => {
    renderDetail({ entryId: 'e1', onOpenEntry: jest.fn() });

    expect(await screen.findByTestId('phone-entry-page')).toBeInTheDocument();
    expect(screen.queryByTestId('entry-settings-panel')).toBeNull();
    expect(screen.queryByTestId('phone-queue-detail')).toBeNull();
  });

  it('waits for the first read before it can say Entry not found', async () => {
    renderDetail({ entryId: 'e1', onOpenEntry: jest.fn() });

    expect(screen.queryByText('Entry not found')).toBeNull();
    expect(await screen.findByTestId('phone-entry-page')).toBeInTheDocument();
    expect(screen.queryByText('Entry not found')).toBeNull();
  });

  it('shows Entry not found for an entry address of a queue with no linked template', async () => {
    queuesMock.getQueue.mockResolvedValue(detail({ linkedTemplateId: null, linkedTemplateName: null }));
    renderDetail({ entryId: 'e1', onOpenEntry: jest.fn() });

    expect(await screen.findByText('Entry not found')).toBeInTheDocument();
  });

  it('changes from the queue detail to the entry page when the entry ID is set', async () => {
    const onOpenEntry = jest.fn();
    const { rerender } = renderDetail({ onOpenEntry });
    await screen.findByRole('button', { name: 'Entry settings for Help All' });

    rerender(<PhoneQueuesView queueId="q1" onOpenQueue={jest.fn()} entryId="e1" onOpenEntry={onOpenEntry} />);

    expect(await screen.findByTestId('phone-entry-page')).toBeInTheDocument();
    expect(screen.queryByTestId('phone-queue-detail')).toBeNull();
  });
});
