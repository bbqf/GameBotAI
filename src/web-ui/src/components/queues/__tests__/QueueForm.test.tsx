import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { QueueForm, QueueFormValue } from '../QueueForm';
import { recoveryFormFromDto } from '../deviceRecoveryForm';

jest.mock('../../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }, { serial: 'emu-2' }], loading: false, error: undefined, refresh: () => {} }),
}));

const baseValue: QueueFormValue = { name: '', emulatorSerial: '', cycleExecution: false, pauseWhenIdle: false, idleThresholdSeconds: 30, emulatorInstanceName: '', emulatorInstanceIndex: null, resumeOnServiceStart: false, smartScheduling: false, smartSchedulingWindowMinutes: 15, ...recoveryFormFromDto(null) };

const renderForm = (overrides: Partial<React.ComponentProps<typeof QueueForm>> = {}) => {
  const onChange = jest.fn();
  const onSubmit = jest.fn();
  const onCancel = jest.fn();
  render(
    <QueueForm
      mode={overrides.mode ?? 'create'}
      value={overrides.value ?? baseValue}
      onChange={onChange}
      onSubmit={onSubmit}
      onCancel={onCancel}
      fieldErrors={overrides.fieldErrors}
      formError={overrides.formError}
      templateControls={overrides.templateControls}
      gameControls={overrides.gameControls}
      running={overrides.running}
    />
  );
  return { onChange, onSubmit, onCancel };
};

describe('QueueForm', () => {
  it('renders the emulator picker in create mode', () => {
    renderForm({ mode: 'create' });
    const emulator = screen.getByLabelText('Emulator *') as HTMLSelectElement;
    expect(emulator.tagName).toBe('SELECT');
    expect(emulator).not.toBeDisabled();
  });

  it('shows the emulator read-only in edit mode without the "cannot be changed" hint', () => {
    renderForm({ mode: 'edit', value: { ...baseValue, name: 'Farm', emulatorSerial: 'emu-9' } });
    const emulator = screen.getByLabelText('Emulator *') as HTMLInputElement;
    expect(emulator.tagName).toBe('INPUT');
    expect(emulator).toHaveValue('emu-9');
    expect(emulator).toBeDisabled();
    expect(screen.queryByText(/bound emulator cannot be changed/i)).not.toBeInTheDocument();
  });

  it('says when the settings apply for a running queue, and keeps the emulator read-only', () => {
    renderForm({ mode: 'edit', running: true, value: { ...baseValue, name: 'Farm', emulatorSerial: 'emu-9' } });
    expect(screen.getByText('Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute.')).toBeInTheDocument();
    expect(screen.getByLabelText('Emulator *')).toBeDisabled();
    expect(screen.getByLabelText('Cycle execution')).not.toBeDisabled();
    expect(screen.getByLabelText('Name *')).not.toBeDisabled();
  });

  it('shows the settings note once, after the Save button', () => {
    renderForm({ mode: 'edit', running: true, value: { ...baseValue, name: 'Farm', emulatorSerial: 'emu-9' } });
    const note = screen.getByText('Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute.');
    const save = screen.getByRole('button', { name: 'Save' });
    expect(screen.getAllByText('Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute.')).toHaveLength(1);
    expect(save.compareDocumentPosition(note) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('shows no settings note for a stopped queue or in create mode', () => {
    renderForm({ mode: 'edit', value: { ...baseValue, name: 'Farm', emulatorSerial: 'emu-9' } });
    expect(screen.queryByText('Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute.')).not.toBeInTheDocument();
  });

  it('renders slots in the new order: emulator → cycle → game → Save → separator → template section', () => {
    renderForm({
      mode: 'edit',
      value: { ...baseValue, name: 'Farm', emulatorSerial: 'emu-9' },
      gameControls: <div data-testid="slot-game" />,
      templateControls: <div data-testid="slot-templates" />,
    });
    const emulator = screen.getByLabelText('Emulator *');
    const cycle = screen.getByLabelText('Cycle execution');
    const game = screen.getByTestId('slot-game');
    const save = screen.getByText('Save');
    const templates = screen.getByTestId('slot-templates');

    const follows = (a: Node, b: Node) =>
      Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(follows(emulator, cycle)).toBe(true);
    expect(follows(cycle, game)).toBe(true);
    expect(follows(game, save)).toBe(true);
    expect(follows(save, templates)).toBe(true);
  });

  it('does not render the edit-only slots in create mode', () => {
    renderForm({
      mode: 'create',
      templateControls: <div data-testid="slot-templates" />,
      gameControls: <div data-testid="slot-game" />,
    });
    expect(screen.queryByTestId('slot-templates')).not.toBeInTheDocument();
    expect(screen.queryByTestId('slot-game')).not.toBeInTheDocument();
  });

  it('renders field and form errors', () => {
    renderForm({ fieldErrors: { name: 'Name is required' }, formError: 'Boom' });
    expect(screen.getByText('Name is required')).toBeInTheDocument();
    expect(screen.getByText('Boom')).toBeInTheDocument();
  });

  it('calls onSubmit when the form is submitted', () => {
    const { onSubmit } = renderForm({ value: { ...baseValue, name: 'Farm', emulatorSerial: 'emu-1' } });
    fireEvent.click(screen.getByText('Save'));
    expect(onSubmit).toHaveBeenCalled();
  });

  it('emits cycle execution toggle changes', () => {
    const { onChange } = renderForm();
    fireEvent.click(screen.getByLabelText('Cycle execution'));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ cycleExecution: true }));
  });

  // ── Feature 073: idle-pause config controls ────────────────────────────────

  it('emits pause-when-idle toggle changes', () => {
    const { onChange } = renderForm();
    fireEvent.click(screen.getByLabelText('Pause game when idle'));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ pauseWhenIdle: true }));
  });

  it('hides the threshold input while idle-pause is disabled', () => {
    renderForm({ value: { ...baseValue, pauseWhenIdle: false } });
    expect(screen.queryByLabelText('Idle threshold seconds')).not.toBeInTheDocument();
  });

  it('shows and edits the threshold input when idle-pause is enabled', () => {
    const { onChange } = renderForm({ value: { ...baseValue, pauseWhenIdle: true, idleThresholdSeconds: 30 } });
    const threshold = screen.getByLabelText('Idle threshold seconds') as HTMLInputElement;
    expect(threshold).toHaveValue(30);
    fireEvent.change(threshold, { target: { value: '45' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ idleThresholdSeconds: 45 }));
  });

  // ── Feature 098: resume after a service restart ─────────────────────────────

  it('reflects and toggles resume-after-service-restart', () => {
    const { onChange } = renderForm({ value: { ...baseValue, resumeOnServiceStart: true } });
    const box = screen.getByLabelText('Resume after service restart') as HTMLInputElement;
    expect(box).toBeChecked();
    fireEvent.click(box);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ resumeOnServiceStart: false }));
  });

  // ── Feature 189: smart scheduling ───────────────────────────────────────────

  it('shows the smart scheduling switch off by default and hides the window', () => {
    renderForm();
    expect(screen.getByLabelText('Smart scheduling')).not.toBeChecked();
    expect(screen.queryByLabelText('Window (minutes)')).not.toBeInTheDocument();
  });

  it('emits the smart scheduling switch change', () => {
    const { onChange } = renderForm();
    fireEvent.click(screen.getByLabelText('Smart scheduling'));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ smartScheduling: true }));
  });

  it('shows the window with 15 minutes when the switch is on, and emits a change as a number', () => {
    const { onChange } = renderForm({ value: { ...baseValue, smartScheduling: true } });
    const window = screen.getByLabelText('Window (minutes)') as HTMLInputElement;
    expect(window).toHaveValue(15);
    fireEvent.change(window, { target: { value: '20' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ smartSchedulingWindowMinutes: 20 }));
  });

  it.each([0, 121])('shows the range message for a window of %i', (minutes) => {
    renderForm({ value: { ...baseValue, smartScheduling: true, smartSchedulingWindowMinutes: minutes } });
    expect(screen.getByText('Enter a value from 1 to 120.')).toBeInTheDocument();
    expect(screen.getByLabelText('Window (minutes)')).toHaveAttribute('aria-invalid', 'true');
  });

  it('shows no range message for a valid window', () => {
    renderForm({ value: { ...baseValue, smartScheduling: true, smartSchedulingWindowMinutes: 120 } });
    expect(screen.queryByText('Enter a value from 1 to 120.')).not.toBeInTheDocument();
  });

  // ── Feature 074: emulator-instance cold-start config controls ───────────────

  it('renders the emulator instance name and index inputs', () => {
    renderForm();
    expect(screen.getByLabelText('Emulator instance name')).toBeInTheDocument();
    expect(screen.getByLabelText('Emulator instance index')).toBeInTheDocument();
  });

  it('emits emulator instance name changes', () => {
    const { onChange } = renderForm();
    fireEvent.change(screen.getByLabelText('Emulator instance name'), { target: { value: 'PNS' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ emulatorInstanceName: 'PNS' }));
  });

  it('emits emulator instance index as a number, and null when cleared', () => {
    const { onChange } = renderForm({ value: { ...baseValue, emulatorInstanceIndex: 2 } });
    const index = screen.getByLabelText('Emulator instance index') as HTMLInputElement;
    expect(index).toHaveValue(2);
    fireEvent.change(index, { target: { value: '5' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ emulatorInstanceIndex: 5 }));
    fireEvent.change(index, { target: { value: '' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ emulatorInstanceIndex: null }));
  });

  // Feature 199: the device recovery option.
  describe('device recovery option', () => {
    it('shows the option off for a new queue, with no fields', () => {
      renderForm();
      expect(screen.getByLabelText('Device recovery')).not.toBeChecked();
      expect(screen.getByText('Reboot the instance when the device is not live')).toBeInTheDocument();
      expect(screen.queryByLabelText('Recovery wait minutes')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Recovery maximum attempts')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Recovery time between attempts minutes')).not.toBeInTheDocument();
    });

    it('emits the change of the option', () => {
      const { onChange } = renderForm();
      fireEvent.click(screen.getByLabelText('Device recovery'));
      expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ deviceRecoveryOn: true }));
    });

    it('shows the three fields with the defaults when the option is on', () => {
      renderForm({ value: { ...baseValue, emulatorInstanceName: 'PNS', deviceRecoveryOn: true } });
      expect(screen.getByLabelText('Recovery wait minutes')).toHaveValue(5);
      expect(screen.getByLabelText('Recovery maximum attempts')).toHaveValue(2);
      expect(screen.getByLabelText('Recovery time between attempts minutes')).toHaveValue(3);
    });

    it('emits the change of each field', () => {
      const { onChange } = renderForm({ value: { ...baseValue, emulatorInstanceName: 'PNS', deviceRecoveryOn: true } });
      fireEvent.change(screen.getByLabelText('Recovery wait minutes'), { target: { value: '3' } });
      expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ recoveryAfterMinutes: 3 }));
      fireEvent.change(screen.getByLabelText('Recovery maximum attempts'), { target: { value: '4' } });
      expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ recoveryMaxAttempts: 4 }));
      fireEvent.change(screen.getByLabelText('Recovery time between attempts minutes'), { target: { value: '0.5' } });
      expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ recoveryCooldownMinutes: 0.5 }));
    });

    it('shows the message at the field for a value that is not permitted', () => {
      renderForm({ value: { ...baseValue, emulatorInstanceName: '', deviceRecoveryOn: true, recoveryAfterMinutes: 0.5, recoveryMaxAttempts: 6, recoveryCooldownMinutes: -1 } });
      expect(screen.getByText('Device recovery needs an emulator instance name.')).toBeInTheDocument();
      expect(screen.getByText('Enter 1 minute or more.')).toBeInTheDocument();
      expect(screen.getByText('Enter a whole number from 1 to 5.')).toBeInTheDocument();
      expect(screen.getByText('Enter 0 minutes or more.')).toBeInTheDocument();
      expect(screen.getByLabelText('Recovery wait minutes')).toHaveAttribute('aria-invalid', 'true');
      expect(screen.getByLabelText('Device recovery')).toHaveAttribute('aria-invalid', 'true');
    });
  });
});
