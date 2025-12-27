import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ReorderableList, ReorderableListItem } from '../ReorderableList';

const items: ReorderableListItem[] = [
  { id: '1', label: 'First' },
  { id: '2', label: 'Second' },
  { id: '3', label: 'Third' },
];

describe('ReorderableList', () => {
  it('moves items up and down', () => {
    const onChange = jest.fn();
    render(<ReorderableList items={items} onChange={onChange} />);
    fireEvent.click(screen.getAllByLabelText('Move down')[0]);
    expect(onChange).toHaveBeenCalledWith([
      { id: '2', label: 'Second' },
      { id: '1', label: 'First' },
      { id: '3', label: 'Third' },
    ]);
  });

  it('deletes an item and emits change', () => {
    const onChange = jest.fn();
    const onDelete = jest.fn();
    render(<ReorderableList items={items} onChange={onChange} onDelete={onDelete} />);
    fireEvent.click(screen.getAllByText('Delete')[1]);
    expect(onDelete).toHaveBeenCalledWith({ id: '2', label: 'Second' }, 1);
    expect(onChange).toHaveBeenCalledWith([
      { id: '1', label: 'First' },
      { id: '3', label: 'Third' },
    ]);
  });

  it('triggers add and edit callbacks when provided', () => {
    const onAdd = jest.fn();
    const onEdit = jest.fn();
    render(<ReorderableList items={items} onChange={() => {}} onAdd={onAdd} onEdit={onEdit} />);
    fireEvent.click(screen.getAllByText('Edit')[0]);
    fireEvent.click(screen.getAllByText('Add item')[0]);
    expect(onEdit).toHaveBeenCalledWith({ id: '1', label: 'First' }, 0);
    expect(onAdd).toHaveBeenCalledTimes(1);
  });
});
