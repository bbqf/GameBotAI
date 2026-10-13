import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { PhoneQueuesView } from '../PhoneQueuesView';
import * as queuesApi from '../../../services/queues';
import * as templatesApi from '../../../services/queueTemplates';

jest.mock('../../../services/queues');
jest.mock('../../../services/queueTemplates');

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;
const templatesMock = templatesApi as jest.Mocked<typeof templatesApi>;

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
  templatesMock.getQueueTemplate.mockResolvedValue(template());
});

describe('PhoneQueuesView list', () => {
  it('shows name, status chip, and one Start or Stop button for each queue', async () => {
    render(<PhoneQueuesView />);
    const card = await screen.findByTestId('queue-card-q1');
    expect(within(card).getByText('Daily')).toBeInTheDocument();
    expect(within(card).getByText('Stopped')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Start' })).toBeInTheDocument();
    const running = screen.getByTestId('queue-card-q2');
    expect(within(running).getByRole('button', { name: 'Stop' })).toBeInTheDocument();
  });

  it('ends a long name with an ellipsis and keeps the full name in the detail', async () => {
    queuesMock.getQueue.mockResolvedValue(detail({ id: 'q2', name: LONG_NAME }));
    render(<PhoneQueuesView />);
    const title = (await screen.findAllByText(LONG_NAME))[0];
    expect(title).toHaveClass('phone-card__title');
    expect(title).toHaveAttribute('title', LONG_NAME);
    fireEvent.click(screen.getByRole('button', { name: `Open ${LONG_NAME}` }));
    expect(await screen.findByRole('heading', { name: LONG_NAME })).toBeInTheDocument();
  });

  it('starts a queue', async () => {
    queuesMock.startQueue.mockResolvedValue(queue({ status: 'Running' }));
    render(<PhoneQueuesView />);
    fireEvent.click(await screen.findByRole('button', { name: 'Start' }));
    await waitFor(() => expect(queuesMock.startQueue).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(within(screen.getByTestId('queue-card-q1')).getByRole('button', { name: 'Stop' })).toBeInTheDocument());
  });

  it('stops a queue', async () => {
    queuesMock.stopQueue.mockResolvedValue(queue({ id: 'q2', name: LONG_NAME, status: 'Stopped' }));
    render(<PhoneQueuesView />);
    const card = await screen.findByTestId('queue-card-q2');
    fireEvent.click(within(card).getByRole('button', { name: 'Stop' }));
    await waitFor(() => expect(queuesMock.stopQueue).toHaveBeenCalledWith('q2'));
  });

  it('disables Start and Stop when the connection is lost', async () => {
    render(<PhoneQueuesView connectionState="lost" />);
    const card = await screen.findByTestId('queue-card-q1');
    expect(within(card).getByRole('button', { name: 'Start' })).toBeDisabled();
    expect(within(screen.getByTestId('queue-card-q2')).getByRole('button', { name: 'Stop' })).toBeDisabled();
  });

  it('shows no create, edit, duplicate, delete, schedule, or reorder control', async () => {
    render(<PhoneQueuesView />);
    await screen.findByTestId('queue-card-q1');
    expect(screen.queryByRole('button', { name: /create|new queue|edit|duplicate|delete|schedule|reorder|move/i })).toBeNull();
  });

  it('reloads the list when the reload signal changes', async () => {
    const { rerender } = render(<PhoneQueuesView reloadSignal={0} />);
    await screen.findByTestId('queue-card-q1');
    rerender(<PhoneQueuesView reloadSignal={1} />);
    await waitFor(() => expect(queuesMock.listQueues).toHaveBeenCalledTimes(2));
  });
});

describe('PhoneQueuesView detail', () => {
  const openFirst = async () => {
    render(<PhoneQueuesView />);
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
    const view = render(<PhoneQueuesView connectionState="lost" />);
    fireEvent.click(await view.findByRole('button', { name: 'Open Daily' }));
    expect(await view.findByRole('switch', { name: 'Help All' })).toBeDisabled();
    expect(view.getAllByRole('button', { name: 'Start' })[0]).toBeDisabled();
  });

  it('goes back to the list', async () => {
    await openFirst();
    fireEvent.click(screen.getByRole('button', { name: /Back/ }));
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
  });
});
