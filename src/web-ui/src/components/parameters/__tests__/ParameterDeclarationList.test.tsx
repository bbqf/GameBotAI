import React, { useState } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ParameterDeclarationList, validateParameterName } from '../ParameterDeclarationList';
import type { ParameterDeclaration } from '../types';

const Harness: React.FC<{ initial?: ParameterDeclaration[] }> = ({ initial = [] }) => {
  const [parameters, setParameters] = useState<ParameterDeclaration[]>(initial);
  return <ParameterDeclarationList parameters={parameters} onChange={setParameters} />;
};

const declaration = (name: string): ParameterDeclaration => ({
  name,
  type: 'text',
  default: null,
  required: false,
  description: '',
});

describe('ParameterDeclarationList', () => {
  it('explains the empty state and points at the built-ins', () => {
    render(<Harness />);

    expect(screen.getByText(/has no parameters/)).toBeInTheDocument();
    expect(screen.getByText(/queue\./)).toBeInTheDocument();
  });

  it('adds a parameter', () => {
    render(<Harness />);

    fireEvent.click(screen.getByText('Add parameter'));

    expect(screen.getByLabelText('Parameter 1 name')).toBeInTheDocument();
  });

  it('edits name, type, default and required', () => {
    render(<Harness initial={[declaration('adbSerial')]} />);

    fireEvent.change(screen.getByLabelText('Parameter 1 type'), { target: { value: 'number' } });
    fireEvent.change(screen.getByLabelText('Parameter 1 default'), { target: { value: '5000' } });
    fireEvent.click(screen.getByLabelText('Parameter 1 required'));

    expect(screen.getByLabelText('Parameter 1 type')).toHaveValue('number');
    expect(screen.getByLabelText('Parameter 1 default')).toHaveValue('5000');
    expect(screen.getByLabelText('Parameter 1 required')).toBeChecked();
  });

  it('reorders parameters', () => {
    render(<Harness initial={[declaration('first'), declaration('second')]} />);

    fireEvent.click(screen.getByLabelText('Move second up'));

    expect(screen.getByLabelText('Parameter 1 name')).toHaveValue('second');
    expect(screen.getByLabelText('Parameter 2 name')).toHaveValue('first');
  });

  it('removes a parameter', () => {
    render(<Harness initial={[declaration('adbSerial')]} />);

    fireEvent.click(screen.getByLabelText('Remove adbSerial'));

    expect(screen.queryByLabelText('Parameter 1 name')).not.toBeInTheDocument();
  });

  it('rejects a reserved name inline once the field is touched', () => {
    render(<Harness initial={[declaration('iteration')]} />);

    fireEvent.blur(screen.getByLabelText('Parameter 1 name'));

    expect(screen.getByRole('alert')).toHaveTextContent(/reserved/);
  });

  it('flags a numeric default that is not a whole number', () => {
    render(
      <Harness
        initial={[{ name: 'waitMs', type: 'number', default: 'soon', required: false, description: '' }]}
      />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent(/whole number/);
  });

  it('warns that a required parameter with no default blocks a queue start', () => {
    render(
      <Harness
        initial={[{ name: 'adbSerial', type: 'text', default: null, required: true, description: '' }]}
      />,
    );

    expect(screen.getByText(/its start is refused/)).toBeInTheDocument();
  });
});

describe('validateParameterName', () => {
  it.each([
    ['', 'Name is required.'],
    ['iteration', "'iteration' is reserved for the loop iteration value."],
    ['queue.emulatorSerial', "Names may not use the reserved 'queue' namespace."],
    ['2fast', 'Use letters, digits and underscore; do not start with a digit.'],
    ['has-dash', 'Use letters, digits and underscore; do not start with a digit.'],
  ])('rejects %s', (name, expected) => {
    expect(validateParameterName(name, [])).toBe(expected);
  });

  it('accepts a valid identifier', () => {
    expect(validateParameterName('adbSerial', [])).toBeUndefined();
  });

  it('rejects a name that differs from an existing one only by case', () => {
    // Names resolve case-sensitively, so two such declarations could never both be reachable.
    expect(validateParameterName('adbserial', ['adbSerial'])).toBe(
      'Another parameter already uses this name.',
    );
  });
});
