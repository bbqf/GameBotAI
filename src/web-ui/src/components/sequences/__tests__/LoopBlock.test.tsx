import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { LoopBlockHeader } from '../LoopBlockHeader';
import { BreakStepRow } from '../BreakStepRow';
import { LoopBlock } from '../LoopBlock';
import type { LoopStepEntry } from '../../../types/stepEntry';
import type { CommandOption } from '../LoopBlock';

const testCommands: CommandOption[] = [
  { value: 'cmd-a', label: 'Command A' },
  { value: 'cmd-b', label: 'Command B' },
];

describe('LoopBlockHeader', () => {
  it('renders count loop header with editable count input and iteration hint', () => {
    render(<LoopBlockHeader loopType="count" count={10} />);
    expect(screen.getByTestId('loop-type-badge')).toHaveTextContent('Count');
    const input = screen.getByTestId('loop-count-input') as HTMLInputElement;
    expect(input.value).toBe('10');
    expect(screen.getByTestId('loop-iteration-hint')).toHaveTextContent('{{iteration}}');
  });

  it('fires onCountChange when count input is changed', () => {
    const onCountChange = jest.fn();
    render(<LoopBlockHeader loopType="count" count={3} onCountChange={onCountChange} />);
    fireEvent.change(screen.getByTestId('loop-count-input'), { target: { value: '7' } });
    expect(onCountChange).toHaveBeenCalledWith(7);
  });

  it('renders while loop header with condition type selector', () => {
    render(
      <LoopBlockHeader
        loopType="while"
        condition={{ type: 'imageVisible', imageId: 'my-img', minSimilarity: null }}
      />
    );
    expect(screen.getByTestId('loop-type-badge')).toHaveTextContent('While');
    expect(screen.getByTestId('loop-condition-type')).toHaveValue('imageVisible');
    expect(screen.getByTestId('loop-condition-imageId')).toHaveValue('my-img');
    expect(screen.getByTestId('loop-iteration-hint')).toBeInTheDocument();
  });

  it('renders repeatUntil loop header without iteration hint', () => {
    render(
      <LoopBlockHeader
        loopType="repeatUntil"
        condition={{ type: 'imageVisible', imageId: 'done', minSimilarity: null }}
      />
    );
    expect(screen.getByTestId('loop-type-badge')).toHaveTextContent('Repeat\u2011Until');
    expect(screen.queryByTestId('loop-iteration-hint')).not.toBeInTheDocument();
  });

  it('renders editable max iterations input', () => {
    const onMaxChange = jest.fn();
    render(<LoopBlockHeader loopType="count" count={5} maxIterations={100} onMaxIterationsChange={onMaxChange} />);
    const input = screen.getByTestId('loop-max-iterations') as HTMLInputElement;
    expect(input.value).toBe('100');
    fireEvent.change(input, { target: { value: '50' } });
    expect(onMaxChange).toHaveBeenCalledWith(50);
  });

  it('renders commandOutcome condition fields for while loop', () => {
    render(
      <LoopBlockHeader
        loopType="while"
        condition={{ type: 'commandOutcome', stepRef: 'step-1', expectedState: 'success' }}
      />
    );
    expect(screen.getByTestId('loop-condition-stepRef')).toHaveValue('step-1');
    expect(screen.getByTestId('loop-condition-expectedState')).toHaveValue('success');
  });

  it('fires onConditionChange when switching condition type', () => {
    const onConditionChange = jest.fn();
    render(
      <LoopBlockHeader
        loopType="while"
        condition={{ type: 'imageVisible', imageId: 'x', minSimilarity: null }}
        onConditionChange={onConditionChange}
      />
    );
    fireEvent.change(screen.getByTestId('loop-condition-type'), { target: { value: 'commandOutcome' } });
    expect(onConditionChange).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'commandOutcome' })
    );
  });

  it('fires onConditionChange when editing imageId', () => {
    const onConditionChange = jest.fn();
    render(
      <LoopBlockHeader
        loopType="while"
        condition={{ type: 'imageVisible', imageId: '', minSimilarity: null }}
        onConditionChange={onConditionChange}
      />
    );
    fireEvent.change(screen.getByTestId('loop-condition-imageId'), { target: { value: 'new-img' } });
    expect(onConditionChange).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'imageVisible', imageId: 'new-img' })
    );
  });

  it('clears max iterations when input is emptied', () => {
    const onMaxChange = jest.fn();
    render(<LoopBlockHeader loopType="count" count={5} maxIterations={100} onMaxIterationsChange={onMaxChange} />);
    fireEvent.change(screen.getByTestId('loop-max-iterations'), { target: { value: '' } });
    expect(onMaxChange).toHaveBeenCalledWith(undefined);
  });

  it('shows empty max input when maxIterations is not set', () => {
    render(<LoopBlockHeader loopType="count" count={5} />);
    const input = screen.getByTestId('loop-max-iterations') as HTMLInputElement;
    expect(input.value).toBe('');
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

  it('renders commandOutcome condition fields when condition is commandOutcome', () => {
    render(
      <BreakStepRow
        breakCondition={{ type: 'commandOutcome', stepRef: 'step-2', expectedState: 'failed' }}
        onChange={() => {}}
        onRemove={() => {}}
      />
    );
    expect(screen.getByTestId('break-condition-editor')).toBeInTheDocument();
    expect(screen.getByText('Step Ref')).toBeInTheDocument();
    expect(screen.getByText('step-2')).toBeInTheDocument();
    expect(screen.getByText('Expected State')).toBeInTheDocument();
    expect(screen.getByText('failed')).toBeInTheDocument();
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
    render(<LoopBlock loop={loop} onChange={onChange} onRemove={() => {}} commandOptions={testCommands} />);
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

  it('deleting an action step from body calls onChange with that step removed', () => {
    const onChange = jest.fn();
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd-a', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
        { type: 'Action', id: 'a2', stepId: 'a2', commandId: 'cmd-b', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
      ],
    };
    render(<LoopBlock loop={loop} onChange={onChange} onRemove={() => {}} commandOptions={testCommands} />);
    const deleteButtons = screen.getAllByText('Delete');
    fireEvent.click(deleteButtons[0]);
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as LoopStepEntry;
    expect(updated.body).toHaveLength(1);
    expect(updated.body[0].id).toBe('a2');
  });

  it('renders command dropdown for action step in body with options', () => {
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd-a', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
      ],
    };
    render(<LoopBlock loop={loop} onChange={() => {}} onRemove={() => {}} commandOptions={testCommands} />);
    const select = screen.getByTestId('loop-body-command-select') as HTMLSelectElement;
    expect(select.value).toBe('cmd-a');
    expect(screen.getByText('Command A')).toBeInTheDocument();
    expect(screen.getByText('Command B')).toBeInTheDocument();
  });

  it('changing command in body step fires onChange with updated commandId', () => {
    const onChange = jest.fn();
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd-a', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
      ],
    };
    render(<LoopBlock loop={loop} onChange={onChange} onRemove={() => {}} commandOptions={testCommands} />);
    fireEvent.change(screen.getByTestId('loop-body-command-select'), { target: { value: 'cmd-b' } });
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as LoopStepEntry;
    expect(updated.body[0].type).toBe('Action');
    expect((updated.body[0] as any).commandId).toBe('cmd-b');
  });

  it('editing break condition inside body calls onChange with updated body', () => {
    const onChange = jest.fn();
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Break', id: 'brk-1', stepId: 'brk-1', breakCondition: { type: 'imageVisible', imageId: 'x', minSimilarity: null } },
      ],
    };
    render(<LoopBlock loop={loop} onChange={onChange} onRemove={() => {}} />);
    // Toggle "Always break" to clear the condition
    fireEvent.click(screen.getByTestId('always-break-toggle'));
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as LoopStepEntry;
    expect(updated.body[0].type).toBe('Break');
    expect((updated.body[0] as any).breakCondition).toBeUndefined();
  });

  it('move up on first step is disabled', () => {
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
      ],
    };
    render(<LoopBlock loop={loop} onChange={() => {}} onRemove={() => {}} />);
    const moveUp = screen.getByLabelText('Move up');
    expect(moveUp).toBeDisabled();
  });

  it('move down on last step is disabled', () => {
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
      ],
    };
    render(<LoopBlock loop={loop} onChange={() => {}} onRemove={() => {}} />);
    const moveDown = screen.getByLabelText('Move down');
    expect(moveDown).toBeDisabled();
  });

  it('removing a break step from body via its Remove button calls onChange without that step', () => {
    const onChange = jest.fn();
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
        { type: 'Break', id: 'brk-1', stepId: 'brk-1', breakCondition: undefined },
      ],
    };
    render(<LoopBlock loop={loop} onChange={onChange} onRemove={() => {}} />);
    fireEvent.click(screen.getByTestId('break-remove'));
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as LoopStepEntry;
    expect(updated.body).toHaveLength(1);
    expect(updated.body[0].id).toBe('a1');
  });

  it('disables all controls when disabled prop is true', () => {
    const loop: LoopStepEntry = {
      ...baseLoop,
      body: [
        { type: 'Action', id: 'a1', stepId: 'a1', commandId: 'cmd', conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success' },
      ],
    };
    render(<LoopBlock loop={loop} onChange={() => {}} onRemove={() => {}} disabled />);
    expect(screen.getByTestId('loop-remove')).toBeDisabled();
    expect(screen.getByTestId('add-body-step')).toBeDisabled();
    expect(screen.getByTestId('add-break-step')).toBeDisabled();
  });
});
