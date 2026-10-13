import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { TargetList } from '../TargetList';
import { QueueLevelTable } from '../QueueLevelTable';
import { QueueDto, listQueues, setQueueNotificationLevel } from '../../../services/queues';
import type { NotificationTargetDto } from '../../../services/notifications';
import { mockViewport, ViewportMock } from '../../../testUtils/viewport';

jest.mock('../../../services/queues');

const mockList = listQueues as jest.MockedFunction<typeof listQueues>;
const mockSet = setQueueNotificationLevel as jest.MockedFunction<typeof setQueueNotificationLevel>;

const target: NotificationTargetDto = {
  id: 't1', type: 'telegram', name: 'My phone', enabled: true,
  settings: { chatId: '-100123' }, hasSecret: true, secretHint: '••••Xy9z'
};

const queue: QueueDto = {
  id: 'q1', name: 'Farm-1', emulatorSerial: 'emu-1', cycleExecution: false, pauseWhenIdle: false,
  idleThresholdSeconds: 30, status: 'Stopped', entryCount: 0, linkedTemplateId: null, linkedGameId: null, linkedGameName: null
};

describe('Notifications on a phone', () => {
  let mock: ViewportMock;
  beforeEach(() => {
    jest.resetAllMocks();
    mock = mockViewport(360, 640);
  });
  afterEach(() => mock.restore());

  it('shows targets as cards, not a table, with all actions', () => {
    const onEdit = jest.fn();
    render(<TargetList targets={[target]} onEdit={onEdit} onDelete={jest.fn()} onTest={jest.fn()} />);
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.getByText('My phone')).toBeInTheDocument();
    expect(screen.getByText(/-100123/)).toBeInTheDocument();
    expect(screen.getByText(/••••Xy9z/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    expect(onEdit).toHaveBeenCalledWith(target);
    expect(screen.getByRole('button', { name: 'Send test message' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
  });

  it('shows queue levels as cards and saves a change', async () => {
    mockList.mockResolvedValue([queue]);
    mockSet.mockResolvedValue({ ...queue, notificationLevel: 'failure' });
    render(<QueueLevelTable />);
    const select = await screen.findByLabelText('Notification level for Farm-1');
    expect(screen.queryByRole('table')).toBeNull();
    fireEvent.change(select, { target: { value: 'failure' } });
    await waitFor(() => expect(mockSet).toHaveBeenCalledWith('q1', 'failure'));
  });
});
