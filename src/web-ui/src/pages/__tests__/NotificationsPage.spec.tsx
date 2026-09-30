import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { NotificationsPage } from '../NotificationsPage';
import {
  NotificationTargetDto,
  createNotificationTarget,
  deleteNotificationTarget,
  listNotificationTargets,
  testNotificationTarget,
  updateNotificationTarget
} from '../../services/notifications';
import { listQueues } from '../../services/queues';

jest.mock('../../services/notifications');
jest.mock('../../services/queues');

const mockList = listNotificationTargets as jest.MockedFunction<typeof listNotificationTargets>;
const mockDelete = deleteNotificationTarget as jest.MockedFunction<typeof deleteNotificationTarget>;
const mockCreate = createNotificationTarget as jest.MockedFunction<typeof createNotificationTarget>;
const mockUpdate = updateNotificationTarget as jest.MockedFunction<typeof updateNotificationTarget>;
const mockTest = testNotificationTarget as jest.MockedFunction<typeof testNotificationTarget>;
const mockQueues = listQueues as jest.MockedFunction<typeof listQueues>;

const target = (over: Partial<NotificationTargetDto> = {}): NotificationTargetDto => ({
  id: 't1',
  type: 'telegram',
  name: 'My phone',
  enabled: true,
  settings: { chatId: '-100' },
  hasSecret: true,
  secretHint: '••••Xy9z',
  ...over
});

describe('NotificationsPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    mockQueues.mockResolvedValue([]);
  });

  it('shows the guide and the empty target hint with no target', async () => {
    mockList.mockResolvedValue([]);

    render(<NotificationsPage />);

    expect(await screen.findByText(/No target is saved/)).toBeInTheDocument();
    expect(screen.getByText('How to set up Telegram')).toBeInTheDocument();
  });

  it('shows a load error', async () => {
    mockList.mockRejectedValue(new Error('offline'));

    render(<NotificationsPage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('offline');
  });

  it('opens the add form and closes it with Cancel', async () => {
    mockList.mockResolvedValue([]);
    render(<NotificationsPage />);
    await screen.findByText(/No target is saved/);

    fireEvent.click(screen.getByRole('button', { name: 'Add target' }));
    expect(screen.getByRole('form', { name: 'Add target' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('form', { name: 'Add target' })).toBeNull();
  });

  it('opens the edit form for a target', async () => {
    mockList.mockResolvedValue([target()]);
    render(<NotificationsPage />);
    await screen.findByText('My phone');

    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    expect(screen.getByRole('form', { name: 'Edit target' })).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('My phone');
  });

  it('deletes a target after the confirmation', async () => {
    mockList.mockResolvedValue([target()]);
    mockDelete.mockResolvedValue(undefined);
    render(<NotificationsPage />);
    await screen.findByText('My phone');

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(mockDelete).toHaveBeenCalledWith('t1'));
    await waitFor(() => expect(screen.queryByText('My phone')).toBeNull());
  });

  it('shows an error when a delete fails', async () => {
    mockList.mockResolvedValue([target()]);
    mockDelete.mockRejectedValue(new Error('HTTP 500'));
    render(<NotificationsPage />);
    await screen.findByText('My phone');

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 500');
    expect(screen.getByText('My phone')).toBeInTheDocument();
  });

  it('sends a test message from the list and shows the result', async () => {
    mockList.mockResolvedValue([target()]);
    mockTest.mockResolvedValueOnce({ ok: true, reason: null });
    render(<NotificationsPage />);
    await screen.findByText('My phone');

    fireEvent.click(screen.getByRole('button', { name: 'Send test message' }));
    expect(await screen.findByRole('status')).toHaveTextContent('The test message was sent.');

    mockTest.mockResolvedValueOnce({ ok: false, reason: 'chat not found' });
    fireEvent.click(screen.getByRole('button', { name: 'Send test message' }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('chat not found'));

    mockTest.mockRejectedValueOnce(new Error('HTTP 502'));
    fireEvent.click(screen.getByRole('button', { name: 'Send test message' }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('HTTP 502'));
  });

  it('adds a saved target to the list and switches the form to edit', async () => {
    mockList.mockResolvedValue([]);
    mockCreate.mockResolvedValue(target({ id: 'new1', name: 'Fresh' }));
    render(<NotificationsPage />);
    await screen.findByText(/No target is saved/);
    fireEvent.click(screen.getByRole('button', { name: 'Add target' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Fresh' } });
    fireEvent.change(screen.getByLabelText('Bot token'), { target: { value: '123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ' } });
    fireEvent.change(screen.getByLabelText('Chat ID'), { target: { value: '-100' } });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('form', { name: 'Edit target' })).toBeInTheDocument();
    expect(screen.getAllByText('Fresh').length).toBeGreaterThan(0);
    expect(screen.queryByText(/No target is saved/)).toBeNull();
  });

  it('replaces an edited target in the list', async () => {
    mockList.mockResolvedValue([target()]);
    mockUpdate.mockResolvedValue(target({ name: 'Renamed' }));
    render(<NotificationsPage />);
    await screen.findByText('My phone');
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Renamed' } });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.getAllByText('Renamed').length).toBeGreaterThan(0));
    expect(mockUpdate).toHaveBeenCalledTimes(1);
  });

  it('closes the form when the edited target is deleted', async () => {
    mockList.mockResolvedValue([target()]);
    mockDelete.mockResolvedValue(undefined);
    render(<NotificationsPage />);
    await screen.findByText('My phone');
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(screen.queryByRole('form', { name: 'Edit target' })).toBeNull());
  });
});
