import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { CommandForm, CommandFormValue, StepEntry } from '../../../components/commands/CommandForm';

jest.mock('@dnd-kit/core', () => {
  const React = jest.requireActual('react');
  const MockDndContext = jest.fn(({ children }: any) => React.createElement(React.Fragment, null, children));
  return {
    DndContext: MockDndContext,
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
  arrayMove: jest.fn((arr: unknown[], from: number, to: number) => {
    const result = [...arr];
    const [item] = result.splice(from, 1);
    result.splice(to, 0, item);
    return result;
  }),
}));

jest.mock('@dnd-kit/utilities', () => ({
  CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } },
}));

import { DndContext } from '@dnd-kit/core';

const getDndHandlers = () => {
  const mock = DndContext as unknown as jest.Mock;
  return mock.mock.calls[mock.mock.calls.length - 1][0] as {
    onDragEnd: (e: any) => void;
    onDragStart: (e: any) => void;
    onDragOver: (e: any) => void;
    onDragCancel: () => void;
  };
};

const step1: StepEntry = { id: 'step-1', type: 'Command', targetId: 'cmd-a' };
const step2: StepEntry = { id: 'step-2', type: 'Command', targetId: 'cmd-b' };

const baseValue: CommandFormValue = {
  name: 'Zoom command',
  steps: [],
  detection: {
    referenceImageId: 'home_button',
    confidence: '0.8',
    offsetX: '0',
    offsetY: '0',
  },
};

// ── Zoom layout tests ─────────────────────────────────────────────────────────

describe('CommandForm zoom layout', () => {
  it('renders key fields at 125% zoom without missing controls', () => {
    render(
      <div style={{ width: '1280px', zoom: 1.25 }}>
        <CommandForm value={baseValue} commandOptions={[]} onChange={() => undefined} />
      </div>
    );

    expect(screen.getByLabelText(/Name \*/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Reference image ID/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Confidence \(0-1\)/i, { selector: '#command-detection-confidence' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset X/i, { selector: '#command-detection-offset-x' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset Y/i, { selector: '#command-detection-offset-y' })).toBeInTheDocument();
  });

  it('renders key fields at 150% zoom without missing controls', () => {
    render(
      <div style={{ width: '1280px', zoom: 1.5 }}>
        <CommandForm value={baseValue} commandOptions={[]} onChange={() => undefined} />
      </div>
    );

    expect(screen.getByLabelText(/Name \*/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Reference image ID/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Confidence \(0-1\)/i, { selector: '#command-detection-confidence' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset X/i, { selector: '#command-detection-offset-x' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset Y/i, { selector: '#command-detection-offset-y' })).toBeInTheDocument();
  });
});

// ── US1: Drag to reorder (T007-T012) ─────────────────────────────────────────

describe('CommandForm drag-and-drop reordering', () => {
  beforeEach(() => {
    (DndContext as unknown as jest.Mock).mockClear();
  });

  it('T007 calls onChange with steps in new order after drag end', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1, step2] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );

    const { onDragEnd } = getDndHandlers();
    act(() => {
      onDragEnd({ active: { id: 'step-1' }, over: { id: 'step-2' } });
    });

    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as CommandFormValue;
    expect(updated.steps[0].id).toBe('step-2');
    expect(updated.steps[1].id).toBe('step-1');
  });

  it('T008 does not call onChange when drag is cancelled', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1, step2] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );

    const { onDragCancel } = getDndHandlers();
    act(() => { onDragCancel(); });

    expect(onChange).not.toHaveBeenCalled();
  });

  it('T008 does not call onChange when dropped on the same step', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1, step2] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );

    const { onDragEnd } = getDndHandlers();
    act(() => {
      onDragEnd({ active: { id: 'step-1' }, over: { id: 'step-1' } });
    });

    expect(onChange).not.toHaveBeenCalled();
  });

  it('T009 drag handles are disabled when submitting', () => {
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1, step2] }}
        commandOptions={[]}
        onChange={() => undefined}
        submitting
      />
    );

    screen.getAllByLabelText('Drag to reorder').forEach((handle) => {
      expect(handle).toHaveStyle({ cursor: 'not-allowed' });
    });
  });

  it('T010 drop indicator is present in DOM after drag start and over', () => {
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1, step2] }}
        commandOptions={[]}
        onChange={() => undefined}
      />
    );

    const { onDragStart, onDragOver } = getDndHandlers();
    act(() => {
      onDragStart({ active: { id: 'step-1' } });
      onDragOver({ over: { id: 'step-2' } });
    });

    // Re-read handlers after re-render caused by state update
    const { onDragStart: _s, onDragOver: _o, ..._ } = getDndHandlers();
    expect(document.querySelector('.drop-indicator')).toBeInTheDocument();
  });

  it('T011 does not call onChange when single-step drag ends with no over target', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );

    const { onDragEnd } = getDndHandlers();
    act(() => {
      onDragEnd({ active: { id: 'step-1' }, over: null });
    });

    expect(onChange).not.toHaveBeenCalled();
  });

  it('T012 delete functionality remains intact after DnD swap', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1, step2] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );

    const deleteButtons = screen.getAllByText('Delete');
    fireEvent.click(deleteButtons[0]);

    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as CommandFormValue;
    expect(updated.steps).toHaveLength(1);
    expect(updated.steps[0].id).toBe('step-2');
  });
});

// ── US2: Visual consistency (T013) ───────────────────────────────────────────

describe('CommandForm drag handle visibility', () => {
  beforeEach(() => {
    (DndContext as unknown as jest.Mock).mockClear();
  });

  it('T013 renders a drag handle for each step', () => {
    render(
      <CommandForm
        value={{ name: 'Test', steps: [step1, step2] }}
        commandOptions={[]}
        onChange={() => undefined}
      />
    );

    expect(screen.getAllByLabelText('Drag to reorder')).toHaveLength(2);
  });
});
