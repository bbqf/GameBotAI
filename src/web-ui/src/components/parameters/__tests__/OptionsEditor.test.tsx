import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { OptionsEditor } from '../OptionsEditor';
import type { ParameterOption } from '../types';

const Harness: React.FC<{ initial: ParameterOption[]; valueType?: 'text' | 'number' }> = ({ initial, valueType = 'text' }) => {
  const [options, setOptions] = useState(initial);
  return (
    <>
      <OptionsEditor options={options} valueType={valueType} onChange={setOptions} />
      <output data-testid="labels">{options.map((o) => o.label).join('|')}</output>
    </>
  );
};

describe('OptionsEditor', () => {
  it('refuses a duplicate label with a clear message and a hint', () => {
    render(<Harness initial={[{ label: 'Fast', value: 'a' }, { label: 'fast', value: 'b' }]} />);

    const alerts = screen.getAllByRole('alert');
    expect(alerts[0]).toHaveTextContent(/used two times/);
    expect(alerts[0]).toHaveTextContent(/Give each option a different label/);
  });

  it('refuses a value that is not a whole number for a number parameter', () => {
    render(<Harness valueType="number" initial={[{ label: 'One', value: 'x' }]} />);

    expect(screen.getByRole('alert')).toHaveTextContent(/whole number/);
  });

  it('refuses an option value that is only a placeholder', () => {
    render(<Harness initial={[{ label: 'A', value: '{{a}}' }]} />);

    expect(screen.getByRole('alert')).toHaveTextContent(/only a placeholder/);
  });

  it('adds, moves, and removes options', () => {
    render(<Harness initial={[{ label: 'A', value: '1' }, { label: 'B', value: '2' }]} />);

    fireEvent.click(screen.getByRole('button', { name: 'Add option' }));
    expect(screen.getByTestId('labels')).toHaveTextContent('A|B|');
    fireEvent.click(screen.getByRole('button', { name: 'Move option 2 up' }));
    expect(screen.getByTestId('labels')).toHaveTextContent('B|A|');
    fireEvent.click(screen.getByRole('button', { name: 'Remove option 1' }));
    expect(screen.getByTestId('labels')).toHaveTextContent('A|');
  });

  it('keeps at least one option and stops at 50', () => {
    const { unmount } = render(<Harness initial={[{ label: 'A', value: '1' }]} />);
    expect(screen.getByRole('button', { name: 'Remove option 1' })).toBeDisabled();
    unmount();

    const fifty = Array.from({ length: 50 }, (_, i) => ({ label: `L${i}`, value: `v${i}` }));
    render(<Harness initial={fifty} />);
    expect(screen.getByRole('button', { name: 'Add option' })).toBeDisabled();
    expect(screen.getByText(/up to 50 options/)).toBeInTheDocument();
  });
});
