import React, { useState } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ParameterizableField } from '../ParameterizableField';
import type { ParameterScopeEntry } from '../types';

const scope: ParameterScopeEntry[] = [
  {
    name: 'queue.emulatorSerial',
    value: null,
    originLayer: 'queue',
    declared: false,
    description: "The executing queue's bound ADB device serial.",
  },
  {
    name: 'waitMs',
    value: '5000',
    originLayer: 'default',
    declared: true,
    description: 'Poll interval before giving up.',
  },
];

/** Wrapper so typing/insertion is exercised against real state, not a stubbed value. */
const Harness: React.FC<{ initial?: string; numeric?: boolean }> = ({ initial = '', numeric }) => {
  const [value, setValue] = useState(initial);
  return (
    <ParameterizableField
      label="ADB serial"
      value={value}
      onChange={setValue}
      scope={scope}
      numeric={numeric}
    />
  );
};

describe('ParameterizableField', () => {
  it('lists in-scope names with their descriptions when the picker is opened', () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText('Insert parameter into ADB serial'));

    expect(screen.getByText('queue.emulatorSerial')).toBeInTheDocument();
    expect(screen.getByText("The executing queue's bound ADB device serial.")).toBeInTheDocument();
    expect(screen.getByText('waitMs')).toBeInTheDocument();
  });

  it('marks queue built-ins so they are distinguishable from declared parameters', () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText('Insert parameter into ADB serial'));

    expect(screen.getByText('built-in')).toBeInTheDocument();
  });

  it('inserts a valid reference without the operator typing braces (SC-004)', () => {
    render(<Harness />);

    // Two interactions from the field: open the picker, choose the name.
    fireEvent.click(screen.getByLabelText('Insert parameter into ADB serial'));
    fireEvent.click(screen.getByText('queue.emulatorSerial'));

    expect(screen.getByLabelText('ADB serial')).toHaveValue('{{queue.emulatorSerial}}');
  });

  it('closes the picker after inserting', () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText('Insert parameter into ADB serial'));
    fireEvent.click(screen.getByText('waitMs'));

    expect(screen.queryByLabelText('Search parameters')).not.toBeInTheDocument();
  });

  it('replaces the whole value for a numeric field, which accepts only a whole-field placeholder', () => {
    render(<Harness initial="1500" numeric />);

    fireEvent.click(screen.getByLabelText('Insert parameter into ADB serial'));
    fireEvent.click(screen.getByText('waitMs'));

    expect(screen.getByLabelText('ADB serial')).toHaveValue('{{waitMs}}');
  });

  it('filters the picker by name and by description', () => {
    render(<Harness />);
    fireEvent.click(screen.getByLabelText('Insert parameter into ADB serial'));

    fireEvent.change(screen.getByLabelText('Search parameters'), { target: { value: 'serial' } });

    expect(screen.getByText('queue.emulatorSerial')).toBeInTheDocument();
    expect(screen.queryByText('waitMs')).not.toBeInTheDocument();
  });

  it('shows an inline error anchored at the field', () => {
    render(
      <ParameterizableField
        label="ADB serial"
        value="{{typo}}"
        onChange={() => {}}
        scope={scope}
        error="'typo' is not declared here and is not a queue built-in."
      />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent("'typo' is not declared here");
    expect(screen.getByLabelText('ADB serial')).toHaveAttribute('aria-invalid', 'true');
  });

  it('disables the insert affordance when nothing is in scope', () => {
    render(<ParameterizableField label="ADB serial" value="" onChange={() => {}} scope={[]} />);

    expect(screen.getByLabelText('Insert parameter into ADB serial')).toBeDisabled();
  });

  it('still allows plain typing, so a literal value needs no parameter', () => {
    render(<Harness />);

    fireEvent.change(screen.getByLabelText('ADB serial'), { target: { value: 'emulator-5558' } });

    expect(screen.getByLabelText('ADB serial')).toHaveValue('emulator-5558');
  });
});
