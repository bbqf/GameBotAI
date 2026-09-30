import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { TargetForm } from '../TargetForm';
import {
  NotificationTargetDto,
  createNotificationTarget,
  testNotificationTarget,
  updateNotificationTarget
} from '../../../services/notifications';

jest.mock('../../../services/notifications');

const mockCreate = createNotificationTarget as jest.MockedFunction<typeof createNotificationTarget>;
const mockUpdate = updateNotificationTarget as jest.MockedFunction<typeof updateNotificationTarget>;
const mockTest = testNotificationTarget as jest.MockedFunction<typeof testNotificationTarget>;

const saved = (over: Partial<NotificationTargetDto> = {}): NotificationTargetDto => ({
  id: 't1',
  type: 'telegram',
  name: 'My phone',
  enabled: true,
  settings: { chatId: '-100' },
  hasSecret: true,
  secretHint: '••••Xy9z',
  ...over
});

const fill = (name: string, token: string, chat: string) => {
  fireEvent.change(screen.getByLabelText('Name'), { target: { value: name } });
  if (token) fireEvent.change(screen.getByLabelText('Bot token'), { target: { value: token } });
  fireEvent.change(screen.getByLabelText('Chat ID'), { target: { value: chat } });
};

describe('TargetForm', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('Save and test calls the create route first and the test route second', async () => {
    const order: string[] = [];
    mockCreate.mockImplementation(async () => { order.push('create'); return saved(); });
    mockTest.mockImplementation(async () => { order.push('test'); return { ok: true, reason: null }; });
    const onSaved = jest.fn();
    render(<TargetForm target={null} onSaved={onSaved} onCancel={jest.fn()} />);
    fill('My phone', '123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ', '-100');

    fireEvent.click(screen.getByRole('button', { name: 'Save and test' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('The test message was sent.'));
    expect(order).toEqual(['create', 'test']);
    expect(mockCreate).toHaveBeenCalledWith({
      type: 'telegram',
      name: 'My phone',
      enabled: true,
      settings: { chatId: '-100' },
      secrets: { botToken: '123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ' }
    });
    expect(mockTest).toHaveBeenCalledWith('t1');
    expect(onSaved).toHaveBeenCalledWith(saved());
  });

  it('a save error skips the test and shows the error', async () => {
    mockCreate.mockRejectedValue(new Error('botToken has a wrong format'));
    render(<TargetForm target={null} onSaved={jest.fn()} onCancel={jest.fn()} />);
    fill('My phone', 'bad', '-100');

    fireEvent.click(screen.getByRole('button', { name: 'Save and test' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('botToken has a wrong format'));
    expect(mockTest).not.toHaveBeenCalled();
  });

  it('an edit with an empty token field sends no token', async () => {
    mockUpdate.mockResolvedValue(saved({ name: 'Renamed' }));
    render(<TargetForm target={saved()} onSaved={jest.fn()} onCancel={jest.fn()} />);
    expect(screen.getByLabelText('Bot token')).toHaveValue('');
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Renamed' } });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(mockUpdate).toHaveBeenCalledTimes(1));
    const [id, input] = mockUpdate.mock.calls[0];
    expect(id).toBe('t1');
    expect(input.name).toBe('Renamed');
    expect(input).not.toHaveProperty('secrets');
    expect(mockCreate).not.toHaveBeenCalled();
    expect(mockTest).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('The target is saved.'));
  });

  it('an edit with a new token sends the token', async () => {
    mockUpdate.mockResolvedValue(saved());
    render(<TargetForm target={saved()} onSaved={jest.fn()} onCancel={jest.fn()} />);
    fireEvent.change(screen.getByLabelText('Bot token'), { target: { value: '654321:ZYXWVUTSRQPONMLKJIHGFEDCBA' } });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(mockUpdate).toHaveBeenCalledTimes(1));
    expect(mockUpdate.mock.calls[0][1].secrets).toEqual({ botToken: '654321:ZYXWVUTSRQPONMLKJIHGFEDCBA' });
  });

  it('shows the reason of a failed test and says that the target stays saved', async () => {
    mockCreate.mockResolvedValue(saved());
    mockTest.mockResolvedValue({ ok: false, reason: 'Telegram answered 400: chat not found' });
    render(<TargetForm target={null} onSaved={jest.fn()} onCancel={jest.fn()} />);
    fill('My phone', '123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ', '-100');

    fireEvent.click(screen.getByRole('button', { name: 'Save and test' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('chat not found'));
    expect(screen.getByRole('status')).toHaveTextContent('The target is saved.');
  });

  it('shows a request error of the test call', async () => {
    mockCreate.mockResolvedValue(saved());
    mockTest.mockRejectedValue(new Error('HTTP 500'));
    render(<TargetForm target={null} onSaved={jest.fn()} onCancel={jest.fn()} />);
    fill('My phone', '123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ', '-100');

    fireEvent.click(screen.getByRole('button', { name: 'Save and test' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('HTTP 500'));
  });

  it('shows the token hint as a placeholder and clears the fields for a new target', () => {
    const { rerender } = render(<TargetForm target={saved()} onSaved={jest.fn()} onCancel={jest.fn()} />);
    expect(screen.getByLabelText('Bot token')).toHaveAttribute('placeholder', expect.stringContaining('Xy9z'));
    expect(screen.getByLabelText('Name')).toHaveValue('My phone');

    rerender(<TargetForm target={null} onSaved={jest.fn()} onCancel={jest.fn()} />);

    expect(screen.getByLabelText('Name')).toHaveValue('');
    expect(screen.getByLabelText('Chat ID')).toHaveValue('');
  });

  it('calls onCancel', () => {
    const onCancel = jest.fn();
    render(<TargetForm target={null} onSaved={jest.fn()} onCancel={onCancel} />);

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onCancel).toHaveBeenCalled();
  });
});
