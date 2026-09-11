import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { DuplicateQueueModal } from '../DuplicateQueueModal';

describe('DuplicateQueueModal', () => {
  it('renders with the pre-filled suggested name', () => {
    render(
      <DuplicateQueueModal
        open
        sourceName="Daily Farming"
        suggestedName="Daily Farming (copy)"
        onConfirm={() => {}}
        onCancel={() => {}}
      />
    );
    const dialog = screen.getByRole('dialog', { name: /duplicate queue/i });
    expect(within(dialog).getByLabelText(/new queue name/i)).toHaveValue('Daily Farming (copy)');
    expect(within(dialog).getByText('Daily Farming')).toBeInTheDocument();
  });

  it('invokes onCancel when Cancel is clicked', () => {
    const onCancel = jest.fn();
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" onConfirm={() => {}} onCancel={onCancel} />
    );
    fireEvent.click(screen.getByText('Cancel'));
    expect(onCancel).toHaveBeenCalled();
  });

  it('submits the pre-filled suggested name on confirm', () => {
    const onConfirm = jest.fn();
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" onConfirm={onConfirm} onCancel={() => {}} />
    );
    fireEvent.click(screen.getByText('Duplicate'));
    expect(onConfirm).toHaveBeenCalledWith('X (copy)');
  });

  it('disables Duplicate when the name is emptied', () => {
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" onConfirm={() => {}} onCancel={() => {}} />
    );
    fireEvent.change(screen.getByLabelText(/new queue name/i), { target: { value: '   ' } });
    expect(screen.getByText('Duplicate')).toBeDisabled();
  });

  it('disables Duplicate when the name is set back to the source name', () => {
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" onConfirm={() => {}} onCancel={() => {}} />
    );
    fireEvent.change(screen.getByLabelText(/new queue name/i), { target: { value: 'X' } });
    expect(screen.getByText('Duplicate')).toBeDisabled();
  });

  it('renders an externally supplied error message', () => {
    render(
      <DuplicateQueueModal
        open
        sourceName="X"
        suggestedName="X (copy)"
        error="name must differ from the original queue's name"
        onConfirm={() => {}}
        onCancel={() => {}}
      />
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/must differ/i);
  });
});
