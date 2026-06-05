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

const makeStep = (id: string, executionStatus: RecordedStep['executionStatus'] = 'idle'): RecordedStep => ({
  id,
  type: 'KeyInput',
  key: 'ENTER',
  label: `Step ${id}`,
  executionStatus,
});

describe('RecordedStepList Run button behavior', () => {
  it('Run button is enabled when step is idle and nothing is executing', () => {
    const steps: RecordedStep[] = [makeStep('a'), makeStep('b')];
    render(
      <RecordedStepList
        steps={steps}
        isExecuting={false}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={jest.fn()}
      />
    );
    const runButtons = screen.getAllByRole('button', { name: /run step/i });
    expect(runButtons[0]).not.toBeDisabled();
    expect(runButtons[1]).not.toBeDisabled();
  });

  it('Run button on step[0] is disabled while step[1] has executionStatus running', () => {
    const steps: RecordedStep[] = [makeStep('a', 'idle'), makeStep('b', 'running')];
    render(
      <RecordedStepList
        steps={steps}
        isExecuting={true}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={jest.fn()}
      />
    );
    const runButtons = screen.getAllByRole('button', { name: /run step/i });
    expect(runButtons[0]).toBeDisabled();
    expect(runButtons[1]).toBeDisabled();
  });

  it('Run button on step[0] is enabled once step[1] completes', () => {
    const steps: RecordedStep[] = [makeStep('a', 'idle'), makeStep('b', 'success')];
    render(
      <RecordedStepList
        steps={steps}
        isExecuting={false}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={jest.fn()}
      />
    );
    const runButtons = screen.getAllByRole('button', { name: /run step/i });
    expect(runButtons[0]).not.toBeDisabled();
    expect(runButtons[1]).not.toBeDisabled();
  });

  it('calls onRunStep with the correct step id when Run is clicked', () => {
    const onRunStep = jest.fn();
    const steps: RecordedStep[] = [makeStep('a'), makeStep('b')];
    render(
      <RecordedStepList
        steps={steps}
        isExecuting={false}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={onRunStep}
      />
    );
    const runButtons = screen.getAllByRole('button', { name: /run step/i });
    fireEvent.click(runButtons[0]);
    expect(onRunStep).toHaveBeenCalledWith('a');
  });

  it('shows spinner icon on the currently running step', () => {
    const steps: RecordedStep[] = [makeStep('a', 'running'), makeStep('b', 'idle')];
    render(
      <RecordedStepList
        steps={steps}
        isExecuting={true}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={jest.fn()}
      />
    );
    const runButtons = screen.getAllByRole('button', { name: /run step/i });
    expect(runButtons[0].textContent).toBe('⏳');
    expect(runButtons[1].textContent).toBe('▶');
  });

  it('shows success badge on step with success status', () => {
    const steps: RecordedStep[] = [makeStep('a', 'success')];
    render(
      <RecordedStepList
        steps={steps}
        isExecuting={false}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={jest.fn()}
      />
    );
    expect(screen.getByTitle('Success')).toBeInTheDocument();
  });

  it('shows error badge with errorMessage as tooltip on error step', () => {
    const step: RecordedStep = { ...makeStep('a', 'error'), errorMessage: 'timeout' };
    render(
      <RecordedStepList
        steps={[step]}
        isExecuting={false}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={jest.fn()}
      />
    );
    const badge = screen.getByTitle('timeout');
    expect(badge).toBeInTheDocument();
    expect(badge.textContent).toBe('✗');
  });

  it('Remove button is disabled while isExecuting', () => {
    const steps: RecordedStep[] = [makeStep('a')];
    render(
      <RecordedStepList
        steps={steps}
        isExecuting={true}
        onRemove={jest.fn()}
        onReorder={jest.fn()}
        onRunStep={jest.fn()}
      />
    );
    const removeButton = screen.getByRole('button', { name: /remove step/i });
    expect(removeButton).toBeDisabled();
  });
});
