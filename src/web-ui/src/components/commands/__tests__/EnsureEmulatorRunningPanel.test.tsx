import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { EnsureEmulatorRunningPanel } from '../EnsureEmulatorRunningPanel';

describe('EnsureEmulatorRunningPanel', () => {
  it('renders instance name, index, and adb serial fields', () => {
    render(<EnsureEmulatorRunningPanel onConfirm={() => {}} onCancel={() => {}} />);
    expect(screen.getByLabelText(/instance name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/instance index/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/adb serial/i)).toBeInTheDocument();
  });

  it('confirms with the entered instance name and serial', () => {
    const onConfirm = jest.fn();
    render(<EnsureEmulatorRunningPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText(/instance name/i), { target: { value: 'LDPlayer-5558' } });
    fireEvent.change(screen.getByLabelText(/adb serial/i), { target: { value: 'emulator-5558' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledWith({
      instanceName: 'LDPlayer-5558',
      instanceIndex: undefined,
      adbSerial: 'emulator-5558',
    });
  });

  it('blocks confirm and shows an error when the serial is missing', () => {
    const onConfirm = jest.fn();
    render(<EnsureEmulatorRunningPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText(/instance name/i), { target: { value: 'LDPlayer-5558' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByText(/adb serial is required/i)).toBeInTheDocument();
  });

  it('blocks confirm when neither instance name nor index is provided', () => {
    const onConfirm = jest.fn();
    render(<EnsureEmulatorRunningPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText(/adb serial/i), { target: { value: 'emulator-5558' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByText(/provide an instance name or index/i)).toBeInTheDocument();
  });

  it('shows Save label and prefilled values when editing', () => {
    render(
      <EnsureEmulatorRunningPanel
        initialValue={{ instanceIndex: '2', adbSerial: 'emulator-5558' }}
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(screen.getByLabelText(/instance index/i)).toHaveValue(2);
    expect(screen.getByLabelText(/adb serial/i)).toHaveValue('emulator-5558');
  });

  it('calls onCancel and not onConfirm when Cancel is clicked', () => {
    const onConfirm = jest.fn();
    const onCancel = jest.fn();
    render(<EnsureEmulatorRunningPanel onConfirm={onConfirm} onCancel={onCancel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
