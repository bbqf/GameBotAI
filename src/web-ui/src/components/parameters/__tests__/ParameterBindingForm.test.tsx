import React, { useState } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ParameterBindingForm } from '../ParameterBindingForm';
import type { ParameterBinding, ParameterDeclaration, ParameterScopeEntry } from '../types';

const declarations: ParameterDeclaration[] = [
  {
    name: 'adbSerial',
    type: 'text',
    default: null,
    required: true,
    description: 'Target emulator.',
  },
  { name: 'waitMs', type: 'number', default: '5000', required: false },
];

const Harness: React.FC<{
  initial?: ParameterBinding[];
  effective?: ParameterScopeEntry[];
  allowAdHoc?: boolean;
}> = ({ initial = [], effective, allowAdHoc }) => {
  const [bindings, setBindings] = useState<ParameterBinding[]>(initial);
  return (
    <ParameterBindingForm
      declarations={declarations}
      bindings={bindings}
      onChange={setBindings}
      effective={effective}
      allowAdHoc={allowAdHoc}
    />
  );
};

describe('ParameterBindingForm', () => {
  it('renders every declared parameter set to inherit, so the common case needs no interaction', () => {
    render(<Harness />);

    expect(screen.getByLabelText('Inherit adbSerial')).toBeChecked();
    expect(screen.getByLabelText('Inherit waitMs')).toBeChecked();
    expect(screen.getByLabelText('Value for adbSerial')).toBeDisabled();
  });

  it('shows the declared description and required flag', () => {
    render(<Harness />);

    expect(screen.getByText('Target emulator.')).toBeInTheDocument();
    expect(screen.getByText('required')).toBeInTheDocument();
  });

  it('lets an explicit value override the inherited one', () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText('Inherit adbSerial'));
    fireEvent.change(screen.getByLabelText('Value for adbSerial'), {
      target: { value: 'emulator-5560' },
    });

    expect(screen.getByLabelText('Value for adbSerial')).toHaveValue('emulator-5560');
    expect(screen.getByLabelText('Value for adbSerial')).toBeEnabled();
  });

  it('previews the effective value and which scope produced it', () => {
    render(
      <Harness
        effective={[
          { name: 'adbSerial', value: 'emulator-5558', originLayer: 'queue', declared: true },
        ]}
      />,
    );

    expect(screen.getByText('emulator-5558')).toBeInTheDocument();
    expect(screen.getByText(/from the queue/)).toBeInTheDocument();
  });

  it('warns when a required parameter is unsatisfied, before anything runs', () => {
    render(
      <Harness effective={[{ name: 'adbSerial', value: null, originLayer: 'entry', declared: true }]} />,
    );

    expect(screen.getByText(/the queue will refuse to start/)).toBeInTheDocument();
  });

  it('falls back to the declared default in the preview when nothing supplies a value', () => {
    render(<Harness />);

    expect(screen.getByText(/Falls back to the declared default/)).toBeInTheDocument();
  });

  it('allows an ad-hoc value on a template entry and marks it as such', () => {
    render(<Harness allowAdHoc />);

    fireEvent.change(screen.getByLabelText('New parameter value name'), {
      target: { value: 'targetSerial' },
    });
    fireEvent.click(screen.getByText('Add'));

    expect(screen.getByText('targetSerial')).toBeInTheDocument();
    expect(screen.getByText('ad-hoc')).toBeInTheDocument();
  });

  it('rejects a reserved ad-hoc name inline', () => {
    render(<Harness allowAdHoc />);

    fireEvent.change(screen.getByLabelText('New parameter value name'), {
      target: { value: 'iteration' },
    });
    fireEvent.click(screen.getByText('Add'));

    expect(screen.getByRole('alert')).toHaveTextContent(/reserved/);
    expect(screen.queryByText('ad-hoc')).not.toBeInTheDocument();
  });

  it('does not offer ad-hoc values where they are not valid', () => {
    render(<Harness />);

    expect(screen.queryByLabelText('New parameter value name')).not.toBeInTheDocument();
  });
});
