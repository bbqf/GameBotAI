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

    expect(screen.getByLabelText('Use default adbSerial')).toBeChecked();
    expect(screen.getByLabelText('Use default waitMs')).toBeChecked();
    expect(screen.getByLabelText('Value for adbSerial')).toBeDisabled();
  });

  it('shows the label "Use default" and never the word "Inherit"', () => {
    render(<Harness />);

    expect(screen.getAllByText('Use default').length).toBe(2);
    expect(screen.queryByText(/Inherit/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^Inherit/)).not.toBeInTheDocument();
  });

  it('shows the declared default next to the box', () => {
    render(<Harness />);

    const row = screen.getByLabelText('Use default waitMs').closest('label') as HTMLElement;
    expect(row.querySelector('.parameter-binding-default')).toHaveTextContent('5000');
  });

  it('shows "No default" for a null default and keeps the box visible', () => {
    render(<Harness />);

    const row = screen.getByLabelText('Use default adbSerial').closest('label') as HTMLElement;
    expect(row.querySelector('.parameter-binding-default')).toHaveTextContent('No default');
  });

  it('shows "No default" for an undefined default, "(empty)" for an empty one, and keeps the name free of the default', () => {
    const decls: ParameterDeclaration[] = [
      { name: 'a', type: 'text', required: false },
      { name: 'b', type: 'text', default: '', required: false },
      { name: 'c', type: 'text', default: 'hello', required: false },
    ];
    render(<ParameterBindingForm declarations={decls} bindings={[]} onChange={() => {}} />);

    const text = (n: string) =>
      (screen.getByLabelText(`Use default ${n}`).closest('label') as HTMLElement).querySelector(
        '.parameter-binding-default',
      );
    expect(text('a')).toHaveTextContent('No default');
    expect(text('b')).toHaveTextContent('(empty)');
    expect(text('c')).toHaveTextContent('hello');
    expect(screen.getByLabelText('Use default c')).toHaveAttribute('aria-label', 'Use default c');
  });

  it('shows the option label for a choice default and the id for an image default', () => {
    const decls: ParameterDeclaration[] = [
      {
        name: 'mode',
        type: 'text',
        kind: 'choice',
        default: 'fast',
        required: false,
        options: [{ label: 'Fast mode', value: 'fast' }],
      },
      { name: 'pic', type: 'text', kind: 'image', default: 'img-1', required: false, allowedImages: ['img-1'] },
    ];
    render(<ParameterBindingForm declarations={decls} bindings={[]} onChange={() => {}} />);

    const text = (n: string) =>
      (screen.getByLabelText(`Use default ${n}`).closest('label') as HTMLElement).querySelector(
        '.parameter-binding-default',
      );
    expect(text('mode')).toHaveTextContent('Fast mode');
    expect(text('pic')).toHaveTextContent('img-1');
  });

  it('shows the declared description and required flag', () => {
    render(<Harness />);

    expect(screen.getByText('Target emulator.')).toBeInTheDocument();
    expect(screen.getByText('required')).toBeInTheDocument();
  });

  it('lets an explicit value override the inherited one', () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText('Use default adbSerial'));
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
