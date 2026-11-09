import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ParameterBindingForm } from '../ParameterBindingForm';
import { readValuesAsList } from '../types';
import type { ParameterBinding, ParameterDeclaration } from '../types';

jest.mock('../../../services/images', () => ({
  getImageBlob: jest.fn(() => Promise.resolve(new Blob(['x']))),
}));

beforeAll(() => {
  (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => 'blob:test';
  (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
});

const declarations: ParameterDeclaration[] = [
  { name: 'quests', type: 'text', kind: 'image', required: false, multiple: true, allowedImages: ['q-a', 'q-b', 'q-c'], default: 'q-a|q-b' },
];

const Harness: React.FC<{ initial?: ParameterBinding[] }> = ({ initial = [] }) => {
  const [bindings, setBindings] = useState<ParameterBinding[]>(initial);
  return (
    <>
      <ParameterBindingForm declarations={declarations} bindings={bindings} onChange={setBindings} />
      <output data-testid="json">{JSON.stringify(bindings)}</output>
    </>
  );
};

describe('ParameterBindingForm with a multi-value parameter', () => {
  it('writes values and the joined value when the person selects images', () => {
    render(<Harness initial={[{ name: 'quests', value: '' }]} />);

    fireEvent.click(screen.getByRole('checkbox', { name: 'q-b' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'q-a' }));

    const saved = JSON.parse(screen.getByTestId('json').textContent ?? '[]') as ParameterBinding[];
    expect(saved[0].values).toEqual(['q-b', 'q-a']);
    expect(saved[0].value).toBe('q-b|q-a');
  });

  it('reads a plain value as a list of one', () => {
    render(<Harness initial={[{ name: 'quests', value: 'q-c' }]} />);

    expect(screen.getByRole('checkbox', { name: 'q-c' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('checkbox', { name: 'q-a' })).toHaveAttribute('aria-checked', 'false');
  });

  it('shows the items of the default next to the Use default box', () => {
    render(<Harness />);

    const row = screen.getByLabelText('Use default quests').closest('label') as HTMLElement;
    expect(row.querySelector('.parameter-binding-default')).toHaveTextContent('q-a, q-b');
  });

  it('keeps Use default for a row with no value', () => {
    render(<Harness />);

    expect(screen.getByLabelText('Use default quests')).toBeChecked();
  });

  it('readValuesAsList gives values first, then a scalar as one item, and nothing for a placeholder', () => {
    expect(readValuesAsList(declarations[0], { name: 'quests', values: ['q-a', 'q-b'] })).toEqual(['q-a', 'q-b']);
    expect(readValuesAsList(declarations[0], { name: 'quests', value: 'q-c' })).toEqual(['q-c']);
    expect(readValuesAsList(declarations[0], { name: 'quests', value: '{{other}}' })).toEqual([]);
    expect(readValuesAsList(declarations[0], { name: 'quests', value: null })).toEqual([]);
    expect(readValuesAsList({ type: 'text', kind: 'image' }, { name: 'x', value: 'a' })).toEqual([]);
  });
});