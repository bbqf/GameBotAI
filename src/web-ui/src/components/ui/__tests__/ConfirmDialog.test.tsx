import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ConfirmDialog } from '../ConfirmDialog';

const renderDialog = (extra: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) => {
  const onConfirm = jest.fn();
  const onCancel = jest.fn();
  render(<ConfirmDialog open title="Delete queue Daily?" message="You cannot undo this." confirmText="Delete" onConfirm={onConfirm} onCancel={onCancel} {...extra} />);
  return { onConfirm, onCancel };
};

describe('ConfirmDialog', () => {
  it('renders nothing when closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('is a modal dialog named by its title', () => {
    renderDialog();
    const dialog = screen.getByRole('dialog', { name: 'Delete queue Daily?' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(screen.getByText('You cannot undo this.')).toBeInTheDocument();
  });

  it('puts the focus on Cancel', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
  });

  it('calls onCancel for Escape and for Cancel, and onConfirm for the confirm button', () => {
    const { onConfirm, onCancel } = renderDialog({ cancelText: 'Keep' });
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(document, { key: 'Enter' });
    expect(onCancel).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('button', { name: 'Keep' }));
    expect(onCancel).toHaveBeenCalledTimes(2);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('uses the danger class for a danger action and primary otherwise', () => {
    const { unmount } = render(<ConfirmDialog open title="T" message="M" confirmText="Delete" danger onConfirm={jest.fn()} onCancel={jest.fn()} />);
    expect(screen.getByRole('button', { name: 'Delete' })).toHaveClass('ui-button--danger');
    unmount();
    renderDialog({ confirmText: 'Replace' });
    expect(screen.getByRole('button', { name: 'Replace' })).toHaveClass('ui-button--primary');
  });

  it('turns both buttons off when busy', () => {
    renderDialog({ busy: true });
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
  });

  it('gives the focus back when it closes', () => {
    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();
    const props = { title: 'T', message: 'M', confirmText: 'OK', onConfirm: jest.fn(), onCancel: jest.fn() };
    const { rerender } = render(<ConfirmDialog open {...props} />);
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
    rerender(<ConfirmDialog open={false} {...props} />);
    expect(trigger).toHaveFocus();
    trigger.remove();
  });

  it('shows the details between the message and the buttons', () => {
    renderDialog({ details: <p data-testid="extra">Careful.</p> });
    const message = screen.getByText('You cannot undo this.');
    const extra = screen.getByTestId('extra');
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    expect(extra.closest('.ui-dialog__details')).not.toBeNull();
    expect(message.compareDocumentPosition(extra) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(extra.compareDocumentPosition(cancel) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('renders no details element when there are no details', () => {
    renderDialog();
    expect(document.querySelector('.ui-dialog__details')).toBeNull();
  });
});
