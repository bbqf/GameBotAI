import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
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
    expect(screen.getByText(/delete Item X/i)).toBeInTheDocument();
    expect(screen.getByText('Referenced by:')).toBeInTheDocument();
    expect(screen.getByText('Cmd One')).toBeInTheDocument();
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
