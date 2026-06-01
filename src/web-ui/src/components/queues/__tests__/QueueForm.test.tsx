import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { QueueForm, QueueFormValue } from '../QueueForm';

jest.mock('../../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }, { serial: 'emu-2' }], loading: false, error: undefined, refresh: () => {} }),
}));

const baseValue: QueueFormValue = { name: '', emulatorSerial: '', cycleExecution: false };

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
      entries={overrides.entries}
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
    renderForm({ mode: 'edit', value: { name: 'Farm', emulatorSerial: 'emu-9', cycleExecution: false } });
    const emulator = screen.getByLabelText('Emulator *') as HTMLInputElement;
    expect(emulator.tagName).toBe('INPUT');
    expect(emulator).toHaveValue('emu-9');
    expect(emulator).toBeDisabled();
    expect(screen.queryByText(/bound emulator cannot be changed/i)).not.toBeInTheDocument();
  });

  it('renders the template-controls and entries slots in row order (emulator -> templates -> cycle -> entries -> actions)', () => {
    renderForm({
      mode: 'edit',
      value: { name: 'Farm', emulatorSerial: 'emu-9', cycleExecution: false },
      templateControls: <div data-testid="slot-templates" />,
      entries: <div data-testid="slot-entries" />,
    });
    const emulator = screen.getByLabelText('Emulator *');
    const templates = screen.getByTestId('slot-templates');
    const cycle = screen.getByLabelText('Cycle execution');
    const entries = screen.getByTestId('slot-entries');
    const actions = screen.getByText('Save');

    const follows = (a: Node, b: Node) =>
      Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(follows(emulator, templates)).toBe(true);
    expect(follows(templates, cycle)).toBe(true);
    expect(follows(cycle, entries)).toBe(true);
    expect(follows(entries, actions)).toBe(true);
  });

  it('does not render the slots in create mode', () => {
    renderForm({
      mode: 'create',
      templateControls: <div data-testid="slot-templates" />,
      entries: <div data-testid="slot-entries" />,
    });
    // Create mode receives no slots from the page; even if passed, the form omits them.
    expect(screen.queryByTestId('slot-templates')).not.toBeInTheDocument();
    expect(screen.queryByTestId('slot-entries')).not.toBeInTheDocument();
  });

  it('renders field and form errors', () => {
    renderForm({ fieldErrors: { name: 'Name is required' }, formError: 'Boom' });
    expect(screen.getByText('Name is required')).toBeInTheDocument();
    expect(screen.getByText('Boom')).toBeInTheDocument();
  });

  it('calls onSubmit when the form is submitted', () => {
    const { onSubmit } = renderForm({ value: { name: 'Farm', emulatorSerial: 'emu-1', cycleExecution: false } });
    fireEvent.click(screen.getByText('Save'));
    expect(onSubmit).toHaveBeenCalled();
  });

  it('emits cycle execution toggle changes', () => {
    const { onChange } = renderForm();
    fireEvent.click(screen.getByLabelText('Cycle execution'));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ cycleExecution: true }));
  });
});
