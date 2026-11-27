import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { SidePanel } from '../SidePanel';

const body = <input aria-label="First" />;

describe('SidePanel', () => {
  it('renders nothing when closed', () => {
    render(<SidePanel open={false} title="Add target" onClose={jest.fn()} mode="desktop">{body}</SidePanel>);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('desktop: is a non-modal dialog named by its title', () => {
    render(<SidePanel open title="Add target" onClose={jest.fn()} mode="desktop" testId="panel">{body}</SidePanel>);
    const dialog = screen.getByRole('dialog', { name: 'Add target' });
    expect(dialog).toHaveAttribute('aria-modal', 'false');
    expect(dialog).toHaveAttribute('data-testid', 'panel');
    expect(dialog).not.toHaveClass('ui-side-panel--page');
  });

  it('moves the focus to the first input and calls onClose for Escape and Close', () => {
    const onClose = jest.fn();
    render(<SidePanel open title="Add target" onClose={onClose} mode="desktop">{body}</SidePanel>);
    expect(screen.getByLabelText('First')).toHaveFocus();
    fireEvent.keyDown(screen.getByLabelText('First'), { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('moves the focus to the panel when the body has no focusable element', () => {
    render(<SidePanel open title="Add target" onClose={jest.fn()} mode="desktop"><p>Text</p></SidePanel>);
    expect(screen.getByRole('dialog')).toHaveFocus();
  });

  it('gives the focus back to the opener on close', () => {
    const Host: React.FC<{ open: boolean }> = ({ open }) => (
      <div>
        <button type="button">Opener</button>
        <SidePanel open={open} title="Add target" onClose={jest.fn()} mode="desktop">{body}</SidePanel>
      </div>
    );
    const { rerender } = render(<Host open={false} />);
    screen.getByRole('button', { name: 'Opener' }).focus();
    rerender(<Host open />);
    expect(screen.getByLabelText('First')).toHaveFocus();
    rerender(<Host open={false} />);
    expect(screen.getByRole('button', { name: 'Opener' })).toHaveFocus();
  });

  it('phone: is a page with a Back button', () => {
    const onClose = jest.fn();
    render(<SidePanel open title="Add target" onClose={onClose} mode="phone">{body}</SidePanel>);
    expect(screen.getByRole('dialog', { name: 'Add target' })).toHaveClass('ui-side-panel--page');
    expect(screen.queryByRole('button', { name: 'Close' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
