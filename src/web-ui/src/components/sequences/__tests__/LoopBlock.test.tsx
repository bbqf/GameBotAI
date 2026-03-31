import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { LoopBlockHeader } from '../LoopBlockHeader';
import { BreakStepRow } from '../BreakStepRow';
import { LoopBlock } from '../LoopBlock';
import type { LoopStepEntry, StepEntry } from '../../../types/stepEntry';

describe('LoopBlockHeader', () => {
  it('renders count loop header with count value and iteration hint', () => {
    render(<LoopBlockHeader loopType="count" count={10} />);
    expect(screen.getByTestId('loop-type-badge')).toHaveTextContent('Count');
    expect(screen.getByTestId('loop-summary')).toHaveTextContent('× 10');
    expect(screen.getByTestId('loop-iteration-hint')).toHaveTextContent('{{iteration}}');
  });

  it('renders while loop header with condition summary', () => {
    render(
      <LoopBlockHeader
        loopType="while"
        condition={{ type: 'imageVisible', imageId: 'my-img', minSimilarity: null }}
      />
    );
    expect(screen.getByTestId('loop-type-badge')).toHaveTextContent('While');
    expect(screen.getByTestId('loop-summary')).toHaveTextContent('imageVisible "my-img"');
    expect(screen.getByTestId('loop-iteration-hint')).toBeInTheDocument();
  });

  it('renders repeatUntil loop header', () => {
    render(
      <LoopBlockHeader
        loopType="repeatUntil"
        condition={{ type: 'imageVisible', imageId: 'done', minSimilarity: null }}
      />
    );
    expect(screen.getByTestId('loop-type-badge')).toHaveTextContent('Repeat\u2011Until');
    expect(screen.queryByTestId('loop-iteration-hint')).not.toBeInTheDocument();
  });

  it('renders max iterations when provided', () => {
    render(<LoopBlockHeader loopType="count" count={5} maxIterations={100} />);
    expect(screen.getByTestId('loop-max-iterations')).toHaveTextContent('max 100');
  });
});

describe('BreakStepRow', () => {
  it('renders unconditional break with Always break checked', () => {
    const onChange = jest.fn();
    render(<BreakStepRow breakCondition={undefined} onChange={onChange} onRemove={() => {}} />);
    const toggle = screen.getByTestId('always-break-toggle') as HTMLInputElement;
    expect(toggle.checked).toBe(true);
    expect(screen.queryByTestId('break-condition-editor')).not.toBeInTheDocument();
  });

  it('toggling Always break off fires onChange with default condition', () => {
    const onChange = jest.fn();
    render(<BreakStepRow breakCondition={undefined} onChange={onChange} onRemove={() => {}} />);
    fireEvent.click(screen.getByTestId('always-break-toggle'));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'imageVisible' })
    );
  });

  it('toggling Always break on fires onChange with undefined', () => {
    const onChange = jest.fn();
    render(
      <BreakStepRow
        breakCondition={{ type: 'imageVisible', imageId: 'x', minSimilarity: null }}
        onChange={onChange}
        onRemove={() => {}}
      />
    );
    expect(screen.getByTestId('break-condition-editor')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('always-break-toggle'));
    expect(onChange).toHaveBeenCalledWith(undefined);
  });

  it('calls onRemove when remove button clicked', () => {
    const onRemove = jest.fn();
    render(<BreakStepRow breakCondition={undefined} onChange={() => {}} onRemove={onRemove} />);
    fireEvent.click(screen.getByTestId('break-remove'));
    expect(onRemove).toHaveBeenCalled();
  });
});

describe('LoopBlock', () => {
  const baseLoop: LoopStepEntry = {
    type: 'Loop',
    id: 'loop-1',
    stepId: 'loop-1',
    loopType: 'count',
    count: 3,
    body: [],
  };

  it('renders loop block with header', () => {
    render(<LoopBlock loop={baseLoop} onChange={() => {}} onRemove={() => {}} />);
    expect(screen.getByTestId('loop-block')).toBeInTheDocument();
    expect(screen.getByTestId('loop-block-header')).toBeInTheDocument();
    expect(screen.getByTestId('loop-body-empty')).toHaveTextContent('No body steps yet.');
  });

  it('renders break step inside loop body', () => {
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Break', id: 'brk-1', stepId: 'brk-1', breakCondition: undefined },
      ],
    };
    render(<LoopBlock loop={loop} onChange={() => {}} onRemove={() => {}} />);
    expect(screen.getByTestId('break-step-row')).toBeInTheDocument();
  });

  it('adding a body step calls onChange with new step appended', () => {
    const onChange = jest.fn();
    render(<LoopBlock loop={baseLoop} onChange={onChange} onRemove={() => {}} />);
    fireEvent.click(screen.getByTestId('add-body-step'));
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as LoopStepEntry;
    expect(updated.body).toHaveLength(1);
    expect(updated.body[0].type).toBe('Action');
  });

  it('adding a break step calls onChange with break appended', () => {
    const onChange = jest.fn();
    render(<LoopBlock loop={baseLoop} onChange={onChange} onRemove={() => {}} />);
    fireEvent.click(screen.getByTestId('add-break-step'));
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as LoopStepEntry;
    expect(updated.body).toHaveLength(1);
    expect(updated.body[0].type).toBe('Break');
  });

  it('reordering a body step fires onChange with updated order', () => {
    const onChange = jest.fn();
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd-a', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
        { type: 'Action', id: 'a2', stepId: 'a2', commandId: 'cmd-b', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
      ],
    };
    render(<LoopBlock loop={loop} onChange={onChange} onRemove={() => {}} />);
    const moveDownButtons = screen.getAllByLabelText('Move down');
    fireEvent.click(moveDownButtons[0]);
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as LoopStepEntry;
    expect(updated.body[0].id).toBe('a2');
    expect(updated.body[1].id).toBe('a1');
  });

  it('calls onRemove when Remove Loop clicked', () => {
    const onRemove = jest.fn();
    render(<LoopBlock loop={baseLoop} onChange={() => {}} onRemove={onRemove} />);
    fireEvent.click(screen.getByTestId('loop-remove'));
    expect(onRemove).toHaveBeenCalled();
  });
});
