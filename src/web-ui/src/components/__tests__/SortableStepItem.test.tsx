import React from 'react';
import { render, screen } from '@testing-library/react';
import { SortableStepItem } from '../SortableStepItem';

// dnd-kit hooks require a DndContext; mock useSortable to isolate the component.
jest.mock('@dnd-kit/sortable', () => ({
  useSortable: jest.fn(() => ({
    attributes: { role: 'button' },
    listeners: { onPointerDown: jest.fn() },
    setNodeRef: jest.fn(),
    transform: null,
    transition: undefined,
    isDragging: false,
  })),
}));

jest.mock('@dnd-kit/utilities', () => ({
  CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } },
}));

import { useSortable } from '@dnd-kit/sortable';

describe('SortableStepItem', () => {
  const defaultProps = { id: 'step-1', scopeId: 'root' };

  it('renders children', () => {
    render(<SortableStepItem {...defaultProps}><span>Step content</span></SortableStepItem>);
    expect(screen.getByText('Step content')).toBeInTheDocument();
  });

  it('renders drag handle icon', () => {
    render(<SortableStepItem {...defaultProps}><span>x</span></SortableStepItem>);
    expect(screen.getByLabelText('Drag to reorder')).toBeInTheDocument();
  });

  it('passes scopeId and type in useSortable data', () => {
    render(<SortableStepItem id="abc" scopeId="loop-123"><span>x</span></SortableStepItem>);
    expect(useSortable).toHaveBeenCalledWith(expect.objectContaining({
      id: 'abc',
      data: { scopeId: 'loop-123', type: 'step' },
    }));
  });

  it('applies not-allowed cursor when disabled', () => {
    render(<SortableStepItem {...defaultProps} disabled><span>x</span></SortableStepItem>);
    const handle = screen.getByLabelText('Drag to reorder');
    expect(handle).toHaveStyle({ cursor: 'not-allowed' });
  });

  it('reduces opacity when isDragging', () => {
    (useSortable as jest.Mock).mockReturnValueOnce({
      attributes: {},
      listeners: {},
      setNodeRef: jest.fn(),
      transform: null,
      transition: undefined,
      isDragging: true,
    });
    const { container } = render(<SortableStepItem {...defaultProps}><span>x</span></SortableStepItem>);
    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.style.opacity).toBe('0.4');
  });
});
