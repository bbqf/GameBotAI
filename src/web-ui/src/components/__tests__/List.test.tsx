import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { List, ListItem } from '../List';

const makeItems = (names: string[]): ListItem[] => names.map((n, i) => ({ id: `id-${i}`, name: n }));

describe('List', () => {
  it('sorts items by name ascending', () => {
    const items = makeItems(['Bravo', 'Alpha', 'Charlie']);
    render(<List items={items} enableFilterThreshold={0} />);
    const rows = screen.getAllByRole('button');
    expect(rows.map(r => r.textContent?.trim())).toEqual(['Alpha', 'Bravo', 'Charlie']);
  });

  it('shows filter input when above threshold and filters by name', () => {
    const items = makeItems(Array.from({ length: 51 }, (_, i) => `Name ${i}`));
    render(<List items={items} enableFilterThreshold={50} />);
    const input = screen.getByLabelText('Filter');
    fireEvent.change(input, { target: { value: 'Name 5' } });
    const rows = screen.getAllByRole('button');
    expect(rows.every(r => r.textContent?.includes('Name 5'))).toBe(true);
  });

  it('renders empty-state message when no items', () => {
    render(<List items={[]} emptyMessage="No items" />);
    expect(screen.getByText('No items')).toBeInTheDocument();
  });
});
