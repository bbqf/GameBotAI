import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueueLevelTable } from '../QueueLevelTable';
import { QueueDto, listQueues, setQueueNotificationLevel } from '../../../services/queues';

jest.mock('../../../services/queues');

const mockList = listQueues as jest.MockedFunction<typeof listQueues>;
const mockSet = setQueueNotificationLevel as jest.MockedFunction<typeof setQueueNotificationLevel>;

const queue = (over: Partial<QueueDto>): QueueDto => ({
  id: 'q1',
  name: 'Farm-1',
  emulatorSerial: 'emu-1',
  cycleExecution: false,
  pauseWhenIdle: false,
  idleThresholdSeconds: 30,
  status: 'Stopped',
  entryCount: 0,
  linkedTemplateId: null,
  linkedGameId: null,
  linkedGameName: null,
  ...over
});

describe('QueueLevelTable', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('shows the level of each queue and none for a queue with no level', async () => {
    mockList.mockResolvedValue([
      queue({ id: 'q1', name: 'Farm-1', notificationLevel: 'failure' }),
      queue({ id: 'q2', name: 'Farm-2' })
    ]);

    render(<QueueLevelTable />);

    expect(await screen.findByLabelText('Notification level for Farm-1')).toHaveValue('failure');
    expect(screen.getByLabelText('Notification level for Farm-2')).toHaveValue('none');
  });

  it('a change calls the level route one time and shows the saved level', async () => {
    mockList.mockResolvedValue([queue({ id: 'q1', name: 'Farm-1', notificationLevel: 'none' })]);
    mockSet.mockResolvedValue(queue({ id: 'q1', name: 'Farm-1', notificationLevel: 'successAndFailure' }));
    render(<QueueLevelTable />);
    const select = await screen.findByLabelText('Notification level for Farm-1');

    fireEvent.change(select, { target: { value: 'successAndFailure' } });

    await waitFor(() => expect(select).toHaveValue('successAndFailure'));
    expect(mockSet).toHaveBeenCalledTimes(1);
    expect(mockSet).toHaveBeenCalledWith('q1', 'successAndFailure');
  });

  it('shows the error and keeps the old level when the save fails', async () => {
    mockList.mockResolvedValue([queue({ id: 'q1', name: 'Farm-1', notificationLevel: 'none' })]);
    mockSet.mockRejectedValue(new Error('Queue not found'));
    render(<QueueLevelTable />);
    const select = await screen.findByLabelText('Notification level for Farm-1');

    fireEvent.change(select, { target: { value: 'failure' } });

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Queue not found'));
    expect(select).toHaveValue('none');
  });

  it('shows a hint when no queue exists and an error when the list fails', async () => {
    mockList.mockResolvedValueOnce([]);
    const { unmount } = render(<QueueLevelTable />);
    expect(await screen.findByText('No queue exists.')).toBeInTheDocument();
    unmount();

    mockList.mockRejectedValueOnce(new Error('offline'));
    render(<QueueLevelTable />);
    expect(await screen.findByRole('alert')).toHaveTextContent('offline');
  });
});
