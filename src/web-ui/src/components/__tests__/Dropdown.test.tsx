import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { Dropdown, Option } from '../Dropdown';

describe('Dropdown', () => {
  const options: Option[] = [
    { value: '1', label: 'One' },
    { value: '2', label: 'Two' },
  ];

  it('renders options and placeholder', () => {
    render(<Dropdown id="dd" label="Select" options={options} value={undefined} onChange={() => {}} />);
    expect(screen.getByLabelText('Select')).toBeInTheDocument();
    expect(screen.getByText('Select…')).toBeInTheDocument();
    expect(screen.getByText('One')).toBeInTheDocument();
    expect(screen.getByText('Two')).toBeInTheDocument();
  });

  it('emits undefined on placeholder selection and value on change', () => {
    const changes: Array<string | undefined> = [];
    render(<Dropdown options={options} value={undefined} onChange={(v) => changes.push(v)} />);
    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: '1' } });
    fireEvent.change(select, { target: { value: '' } });
    expect(changes).toEqual(['1', undefined]);
  });

  it('sets aria-invalid when error provided', () => {
    render(<Dropdown options={options} value={undefined} onChange={() => {}} error="Required" describedById="dd-err" />);
    const select = screen.getByRole('combobox');
    expect(select).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('alert')).toHaveTextContent('Required');
  });
});
