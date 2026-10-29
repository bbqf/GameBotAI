import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ChoiceField } from '../fields/ChoiceField';
import type { ParameterDeclaration } from '../types';

const declaration = (count: number): ParameterDeclaration => ({
  name: 'mode',
  type: 'text',
  kind: 'choice',
  required: false,
  options: Array.from({ length: count }, (_, i) => ({ label: `Label ${i + 1}`, value: `v${i + 1}` })),
});

const Harness: React.FC<{ count: number; initial?: string }> = ({ count, initial = '' }) => {
  const [value, setValue] = useState(initial);
  return (
    <>
      <ChoiceField declaration={declaration(count)} value={value} onChange={setValue} ariaLabel="Value for mode" />
      <output data-testid="value">{value}</output>
    </>
  );
};

describe('ChoiceField', () => {
  it('shows labels only, as buttons for six options or fewer, and stores the technical value', () => {
    render(<Harness count={3} />);

    expect(screen.getAllByRole('radio')).toHaveLength(3);
    expect(screen.queryByText('v2')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio', { name: 'Label 2' }));
    expect(screen.getByTestId('value')).toHaveTextContent('v2');
  });

  it('moves the selection with the arrow keys', () => {
    render(<Harness count={3} initial="v1" />);

    fireEvent.keyDown(screen.getByRole('radio', { name: 'Label 1' }), { key: 'ArrowRight' });

    expect(screen.getByTestId('value')).toHaveTextContent('v2');
  });

  it('shows a drop-down for more than six options', () => {
    render(<Harness count={8} />);

    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox', { name: 'Value for mode' }), { target: { value: 'v5' } });
    expect(screen.getByTestId('value')).toHaveTextContent('v5');
  });

  it('shows a search box only for more than 12 options', () => {
    const { unmount } = render(<Harness count={13} />);
    expect(screen.getByRole('searchbox')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'Label 13' } });
    expect(screen.getAllByRole('option').filter((o) => (o as HTMLOptionElement).value !== '')).toHaveLength(1);
    unmount();

    render(<Harness count={8} />);
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
  });

  it('shows "option no longer exists" for a value that is not an option', () => {
    render(<Harness count={3} initial="gone" />);

    expect(screen.getByRole('alert')).toHaveTextContent(/no longer exists/);
    expect(screen.getByRole('alert')).toHaveTextContent(/Select one of the options/);
  });
});
