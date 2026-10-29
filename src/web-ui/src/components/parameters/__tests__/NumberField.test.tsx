import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { NumberField } from '../fields/NumberField';
import type { ParameterDeclaration } from '../types';

const declaration: ParameterDeclaration = {
  name: 'speed', type: 'number', required: false, min: 1, max: 9, unit: 'x',
};

const Harness: React.FC<{ initial?: string }> = ({ initial = '' }) => {
  const [value, setValue] = useState(initial);
  const [calls, setCalls] = useState(0);
  return (
    <>
      <NumberField
        declaration={declaration}
        value={value}
        onChange={(next) => { setValue(next); setCalls((c) => c + 1); }}
        ariaLabel="Value for speed"
      />
      <output data-testid="value">{value}</output>
      <output data-testid="calls">{calls}</output>
    </>
  );
};

describe('NumberField', () => {
  it('shows the unit and the range hint', () => {
    render(<Harness />);

    expect(screen.getByText('x')).toBeInTheDocument();
    expect(screen.getByText('Enter a number from 1 to 9.')).toBeInTheDocument();
  });

  it('accepts a number in range', () => {
    render(<Harness />);

    fireEvent.change(screen.getByRole('textbox', { name: 'Value for speed' }), { target: { value: '5' } });

    expect(screen.getByTestId('value')).toHaveTextContent('5');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows a message with a hint for a new value out of range, and does not accept it', () => {
    render(<Harness initial="5" />);

    fireEvent.change(screen.getByRole('textbox', { name: 'Value for speed' }), { target: { value: '12' } });

    expect(screen.getByRole('alert')).toHaveTextContent(/above the maximum 9/);
    expect(screen.getByRole('alert')).toHaveTextContent(/Enter a number of 9 or less/);
    expect(screen.getByTestId('value')).toHaveTextContent('5');
    expect(screen.getByTestId('calls')).toHaveTextContent('0');
  });

  it('shows a stored value that is out of range with a message and does not change it', () => {
    render(<Harness initial="30" />);

    expect(screen.getByRole('alert')).toHaveTextContent(/above the maximum 9/);
    expect(screen.getByRole('textbox', { name: 'Value for speed' })).toHaveValue('30');
    expect(screen.getByTestId('calls')).toHaveTextContent('0');
  });

  it('has a screen reader name and links the hint to the field', () => {
    render(<Harness />);

    const input = screen.getByRole('textbox', { name: 'Value for speed' });
    expect(input).toHaveAttribute('aria-describedby');
  });
});
