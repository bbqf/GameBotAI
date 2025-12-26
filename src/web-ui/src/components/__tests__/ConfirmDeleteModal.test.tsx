import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { ConfirmDeleteModal } from '../ConfirmDeleteModal';

describe('ConfirmDeleteModal', () => {
  it('renders when open with item name and references', () => {
    const refs = {
      Commands: [
        { id: 'c1', name: 'Cmd One' },
      ],
    };
    render(
      <ConfirmDeleteModal
        open
        itemName="Item X"
        references={refs}
        onConfirm={() => {}}
        onCancel={() => {}}
      />
    );
    const dialog = screen.getByRole('dialog', { name: /confirm delete/i });
    expect(within(dialog).getByText(/are you sure you want to delete/i)).toBeInTheDocument();
    expect(within(dialog).getByText('Item X')).toBeInTheDocument();
    expect(within(dialog).getByText('Referenced by:')).toBeInTheDocument();
    expect(within(dialog).getByText('Cmd One')).toBeInTheDocument();
  });

  it('invokes callbacks on buttons', () => {
    const onConfirm = jest.fn();
    const onCancel = jest.fn();
    render(
      <ConfirmDeleteModal open itemName="X" onConfirm={onConfirm} onCancel={onCancel} />
    );
    fireEvent.click(screen.getByText('Cancel'));
    fireEvent.click(screen.getByText('Delete'));
    expect(onCancel).toHaveBeenCalled();
    expect(onConfirm).toHaveBeenCalled();
  });
});
