import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';

describe('SearchableDropdown', () => {
  const options: SearchableOption[] = [
    { value: 'a', label: 'Alpha' },
    { value: 'b', label: 'Beta' },
    { value: 'c', label: 'Gamma' },
  ];

  it('filters options by search query', () => {
    render(<SearchableDropdown label="Ref" id="ref" options={options} value={undefined} onChange={() => {}} />);
    const search = screen.getByLabelText('Ref search');
    fireEvent.change(search, { target: { value: 'be' } });
    const select = screen.getByLabelText('Ref');
    const renderedOptions = Array.from(select.querySelectorAll('option')).map((o) => o.textContent);
    expect(renderedOptions).toEqual(['Select…', 'Beta']);
  });

  it('emits value changes including clearing selection', () => {
    const changes: Array<string | undefined> = [];
    render(<SearchableDropdown options={options} value={undefined} onChange={(v) => changes.push(v)} />);
    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: 'a' } });
    fireEvent.change(select, { target: { value: '' } });
    expect(changes).toEqual(['a', undefined]);
  });

  it('invokes create-new callback when provided', () => {
    const create = jest.fn();
    render(<SearchableDropdown options={options} value={undefined} onChange={() => {}} onCreateNew={create} createLabel="Create" />);
    fireEvent.click(screen.getByText('Create'));
    expect(create).toHaveBeenCalledTimes(1);
  });
});
