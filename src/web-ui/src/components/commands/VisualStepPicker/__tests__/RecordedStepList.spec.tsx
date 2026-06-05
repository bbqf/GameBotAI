import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { RecordedStepList } from '../RecordedStepList';
import type { RecordedStep } from '../../../../types/picker';

jest.mock('@dnd-kit/core', () => {
  const React = jest.requireActual('react');
  return {
    DndContext: ({ children }: any) => React.createElement(React.Fragment, null, children),
    PointerSensor: class {},
    useSensor: jest.fn(),
    useSensors: jest.fn(() => []),
  };
});

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
  CSS: { Translate: { toString: () => '' } },
}));

const steps: RecordedStep[] = [
  { id: 'a', type: 'PrimitiveTap', imageId: 'img1', offsetX: 5, offsetY: -3, label: 'img1 (+5, -3)', executionStatus: 'idle' },
  { id: 'b', type: 'KeyInput', key: 'ENTER', label: 'Key: ENTER', executionStatus: 'idle' },
];

describe('RecordedStepList', () => {
  it('renders a label for each step', () => {
    render(<RecordedStepList steps={steps} isExecuting={false} onRemove={jest.fn()} onReorder={jest.fn()} onRunStep={jest.fn()} />);
    expect(screen.getByText('img1 (+5, -3)')).toBeInTheDocument();
    expect(screen.getByText('Key: ENTER')).toBeInTheDocument();
  });

  it('calls onRemove with the correct step id when delete is clicked', () => {
    const onRemove = jest.fn();
    render(<RecordedStepList steps={steps} isExecuting={false} onRemove={onRemove} onReorder={jest.fn()} onRunStep={jest.fn()} />);
    const removeButtons = screen.getAllByRole('button', { name: /remove step/i });
    fireEvent.click(removeButtons[0]);
    expect(onRemove).toHaveBeenCalledWith('a');
  });

  it('renders empty list without errors', () => {
    render(<RecordedStepList steps={[]} isExecuting={false} onRemove={jest.fn()} onReorder={jest.fn()} onRunStep={jest.fn()} />);
    expect(screen.getByRole('list')).toBeInTheDocument();
  });
});
