import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { DuplicateQueueModal } from '../DuplicateQueueModal';

jest.mock('../../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }, { serial: 'emu-2' }], loading: false, error: undefined, refresh: () => {} }),
}));

const sourceEmulator = { emulatorSerial: 'emu-1', emulatorInstanceName: 'PNS', emulatorInstanceIndex: 2 };

describe('DuplicateQueueModal', () => {
  it('renders with the pre-filled suggested name and source emulator', () => {
    render(
      <DuplicateQueueModal
        open
        sourceName="Daily Farming"
        suggestedName="Daily Farming (copy)"
        sourceEmulator={sourceEmulator}
        onConfirm={() => {}}
        onCancel={() => {}}
      />
    );
    const dialog = screen.getByRole('dialog', { name: /duplicate queue/i });
    expect(within(dialog).getByLabelText(/new queue name/i)).toHaveValue('Daily Farming (copy)');
    expect(within(dialog).getByText('Daily Farming')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/emulator instance name/i)).toHaveValue('PNS');
    expect(within(dialog).getByLabelText(/emulator instance index/i)).toHaveValue(2);
  });

  it('invokes onCancel when Cancel is clicked', () => {
    const onCancel = jest.fn();
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" sourceEmulator={sourceEmulator} onConfirm={() => {}} onCancel={onCancel} />
    );
    fireEvent.click(screen.getByText('Cancel'));
    expect(onCancel).toHaveBeenCalled();
  });

  it('submits the pre-filled suggested name and unchanged source emulator on confirm', () => {
    const onConfirm = jest.fn();
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" sourceEmulator={sourceEmulator} onConfirm={onConfirm} onCancel={() => {}} />
    );
    fireEvent.click(screen.getByText('Duplicate'));
    expect(onConfirm).toHaveBeenCalledWith('X (copy)', sourceEmulator);
  });

  it('allows submitting a different emulator instance name/index than the source', () => {
    const onConfirm = jest.fn();
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" sourceEmulator={sourceEmulator} onConfirm={onConfirm} onCancel={() => {}} />
    );
    fireEvent.change(screen.getByLabelText(/emulator instance name/i), { target: { value: 'Other' } });
    fireEvent.change(screen.getByLabelText(/emulator instance index/i), { target: { value: '5' } });
    fireEvent.click(screen.getByText('Duplicate'));
    expect(onConfirm).toHaveBeenCalledWith('X (copy)', {
      emulatorSerial: 'emu-1',
      emulatorInstanceName: 'Other',
      emulatorInstanceIndex: 5,
    });
  });

  it('disables Duplicate when the name is emptied', () => {
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" sourceEmulator={sourceEmulator} onConfirm={() => {}} onCancel={() => {}} />
    );
    fireEvent.change(screen.getByLabelText(/new queue name/i), { target: { value: '   ' } });
    expect(screen.getByText('Duplicate')).toBeDisabled();
  });

  it('disables Duplicate when the name is set back to the source name', () => {
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" sourceEmulator={sourceEmulator} onConfirm={() => {}} onCancel={() => {}} />
    );
    fireEvent.change(screen.getByLabelText(/new queue name/i), { target: { value: 'X' } });
    expect(screen.getByText('Duplicate')).toBeDisabled();
  });

  it('still allows Duplicate when the emulator is left unchanged (unlike the name)', () => {
    const onConfirm = jest.fn();
    render(
      <DuplicateQueueModal open sourceName="X" suggestedName="X (copy)" sourceEmulator={sourceEmulator} onConfirm={onConfirm} onCancel={() => {}} />
    );
    expect(screen.getByText('Duplicate')).not.toBeDisabled();
    fireEvent.click(screen.getByText('Duplicate'));
    expect(onConfirm).toHaveBeenCalledWith('X (copy)', sourceEmulator);
  });

  it('renders an externally supplied error message', () => {
    render(
      <DuplicateQueueModal
        open
        sourceName="X"
        suggestedName="X (copy)"
        sourceEmulator={sourceEmulator}
        error="name must differ from the original queue's name"
        onConfirm={() => {}}
        onCancel={() => {}}
      />
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/must differ/i);
  });
});
