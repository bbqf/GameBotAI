import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
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
  it('shows the empty text', () => {
    render(<TargetList targets={[]} state={{ kind: 'empty', text: 'No target is saved. Follow the guide and add one.' }} onEdit={jest.fn()} onDelete={jest.fn()} />);

    expect(screen.getByText(/No target is saved/)).toBeInTheDocument();
  });

  it('shows the loading text and the error with Try again', () => {
    const onRetry = jest.fn();
    const { rerender } = render(<TargetList targets={[]} state={{ kind: 'loading' }} onEdit={jest.fn()} onDelete={jest.fn()} />);
    expect(screen.getByText('Loading…')).toBeInTheDocument();

    rerender(<TargetList targets={[]} state={{ kind: 'error', text: 'offline', onRetry }} onEdit={jest.fn()} onDelete={jest.fn()} />);
    expect(screen.getByRole('alert')).toHaveTextContent('offline');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalled();
  });

  it('lists the target with the chip, the chat ID, and the token hint but no token', () => {
    render(<TargetList targets={[target()]} onEdit={jest.fn()} onDelete={jest.fn()} />);

    expect(screen.getByText('My phone')).toBeInTheDocument();
    expect(screen.getByText('Telegram')).toBeInTheDocument();
    expect(screen.getByText('-100123')).toBeInTheDocument();
    expect(screen.getByText('Saved (••••Xy9z)')).toBeInTheDocument();
    expect(screen.getByText('On')).toBeInTheDocument();
    expect(screen.getByText('On').closest('.ui-chip')).toHaveClass('ui-chip--done');
  });

  it('shows Saved for a token with no hint', () => {
    render(<TargetList targets={[target({ secretHint: null })]} onEdit={jest.fn()} onDelete={jest.fn()} />);

    expect(screen.getByText('Saved')).toBeInTheDocument();
  });

  it('shows Not set, a dash, and an Off chip for a disabled target with no secret and no chat ID', () => {
    render(<TargetList targets={[target({ hasSecret: false, secretHint: null, enabled: false, settings: {} })]} onEdit={jest.fn()} onDelete={jest.fn()} />);

    expect(screen.getByText('Not set')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.getByText('Off').closest('.ui-chip')).toHaveClass('ui-chip--stopped');
  });

  it('calls onEdit with the target', () => {
    const onEdit = jest.fn();
    const item = target();
    render(<TargetList targets={[item]} onEdit={onEdit} onDelete={jest.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Edit My phone' }));

    expect(onEdit).toHaveBeenCalledWith(item);
  });

  it('calls onDelete with the target from the menu item Delete', () => {
    const onDelete = jest.fn();
    const item = target();
    render(<TargetList targets={[item]} onEdit={jest.fn()} onDelete={onDelete} />);

    fireEvent.click(screen.getByRole('button', { name: 'More actions for My phone' }));
    fireEvent.click(screen.getByRole('menuitem', { name: /Delete/ }));

    expect(onDelete).toHaveBeenCalledWith(item);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('calls onTest with the id', () => {
    const onTest = jest.fn();
    render(<TargetList targets={[target()]} onEdit={jest.fn()} onDelete={jest.fn()} onTest={onTest} />);

    fireEvent.click(screen.getByRole('button', { name: 'Send test message My phone' }));

    expect(onTest).toHaveBeenCalledWith('t1');
  });

  it('turns the test icon off for the target whose test runs', () => {
    render(<TargetList targets={[target(), target({ id: 't2', name: 'Other' })]} onEdit={jest.fn()} onDelete={jest.fn()} onTest={jest.fn()} testingId="t1" />);

    expect(screen.getByRole('button', { name: 'Send test message My phone' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Send test message Other' })).toBeEnabled();
  });
});
