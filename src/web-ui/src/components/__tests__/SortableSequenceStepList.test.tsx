import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { SortableSequenceStepList } from '../SortableSequenceStepList';
import type { ReorderableListItem } from '../ReorderableList';

jest.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  verticalListSortingStrategy: {},
  useSortable: () => ({
    attributes: {},
    listeners: {},
    setNodeRef: jest.fn(),
    transform: null,
    transition: undefined,
    isDragging: false,
  }),
}));

jest.mock('@dnd-kit/utilities', () => ({
  CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } },
}));

const items: ReorderableListItem[] = [
  { id: '1', label: 'Step 1' },
  { id: '2', label: 'Step 2', description: 'desc' },
  { id: '3', label: 'Step 3', details: <span data-testid="detail-3">detail</span> },
];

describe('SortableSequenceStepList', () => {
  it('renders all item labels', () => {
    render(<SortableSequenceStepList items={items} onDelete={() => {}} />);
    expect(screen.getByText('Step 1')).toBeInTheDocument();
    expect(screen.getByText('Step 2')).toBeInTheDocument();
    expect(screen.getByText('Step 3')).toBeInTheDocument();
  });

  it('renders description when provided', () => {
    render(<SortableSequenceStepList items={items} onDelete={() => {}} />);
    expect(screen.getByText('desc')).toBeInTheDocument();
  });

  it('renders details when provided', () => {
    render(<SortableSequenceStepList items={items} onDelete={() => {}} />);
    expect(screen.getByTestId('detail-3')).toBeInTheDocument();
  });

  it('calls onDelete with the correct item when Delete is clicked', () => {
    const onDelete = jest.fn();
    render(<SortableSequenceStepList items={items} onDelete={onDelete} />);
    const deleteButtons = screen.getAllByText('Delete');
    fireEvent.click(deleteButtons[1]);
    expect(onDelete).toHaveBeenCalledWith(items[1]);
  });

  it('shows empty state message when items list is empty', () => {
    render(<SortableSequenceStepList items={[]} onDelete={() => {}} emptyMessage="Nothing here" />);
    expect(screen.getByText('Nothing here')).toBeInTheDocument();
  });

  it('delete buttons are disabled when disabled prop is true', () => {
    render(<SortableSequenceStepList items={items} onDelete={() => {}} disabled />);
    screen.getAllByText('Delete').forEach((btn) => expect(btn).toBeDisabled());
  });

  it('renders a drag handle for each item', () => {
    render(<SortableSequenceStepList items={items} onDelete={() => {}} />);
    expect(screen.getAllByLabelText('Drag to reorder')).toHaveLength(items.length);
  });
});
