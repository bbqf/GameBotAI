import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { TargetList } from '../TargetList';
import type { NotificationTargetDto } from '../../../services/notifications';

const target = (over: Partial<NotificationTargetDto> = {}): NotificationTargetDto => ({
  id: 't1',
  type: 'telegram',
  name: 'My phone',
  enabled: true,
  settings: { chatId: '-100123' },
  hasSecret: true,
  secretHint: '••••Xy9z',
  ...over
});

describe('TargetList', () => {
  it('shows a hint when no target is saved', () => {
    render(<TargetList targets={[]} onEdit={jest.fn()} onDelete={jest.fn()} />);

    expect(screen.getByText(/No target is saved/)).toBeInTheDocument();
  });

  it('lists the target with the chat ID and the token hint but no token', () => {
    render(<TargetList targets={[target()]} onEdit={jest.fn()} onDelete={jest.fn()} />);

    expect(screen.getByText('My phone')).toBeInTheDocument();
    expect(screen.getByText('-100123')).toBeInTheDocument();
    expect(screen.getByText('••••Xy9z')).toBeInTheDocument();
    expect(screen.getByText('Yes')).toBeInTheDocument();
  });

  it('shows Not set for a target with no secret and No for a disabled target', () => {
    render(<TargetList targets={[target({ hasSecret: false, secretHint: null, enabled: false })]} onEdit={jest.fn()} onDelete={jest.fn()} />);

    expect(screen.getByText('Not set')).toBeInTheDocument();
    expect(screen.getByText('No')).toBeInTheDocument();
  });

  it('calls onEdit with the target', () => {
    const onEdit = jest.fn();
    const item = target();
    render(<TargetList targets={[item]} onEdit={onEdit} onDelete={jest.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    expect(onEdit).toHaveBeenCalledWith(item);
  });

  it('asks for a confirmation before a delete', () => {
    const onDelete = jest.fn();
    render(<TargetList targets={[target()]} onEdit={jest.fn()} onDelete={onDelete} />);

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(onDelete).not.toHaveBeenCalled();
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

    expect(onDelete).toHaveBeenCalledWith('t1');
  });

  it('does not delete when the confirmation is cancelled', () => {
    const onDelete = jest.fn();
    render(<TargetList targets={[target()]} onEdit={jest.fn()} onDelete={onDelete} />);

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    expect(onDelete).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('calls onTest when the test button is shown and pressed', () => {
    const onTest = jest.fn();
    render(<TargetList targets={[target()]} onEdit={jest.fn()} onDelete={jest.fn()} onTest={onTest} />);

    fireEvent.click(screen.getByRole('button', { name: 'Send test message' }));

    expect(onTest).toHaveBeenCalledWith('t1');
  });
});
