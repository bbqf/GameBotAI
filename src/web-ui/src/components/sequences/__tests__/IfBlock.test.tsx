import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { IfBlock } from '../IfBlock';
import { IfBlockHeader } from '../IfBlockHeader';
import type { IfStepEntry } from '../../../types/stepEntry';
import type { CommandOption } from '../IfBlock';

// IfBlock uses SortableContext and SortableStepItem (dnd-kit).
// Mock dnd-kit to avoid needing a full DndContext in unit tests.
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

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange, disabled, 'data-testid': testId }: {
    id?: string; label?: string; value: string; onChange: (v: string) => void; disabled?: boolean;
    'data-testid'?: string;
  }) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} data-testid={testId} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

const testCommands: CommandOption[] = [
  { value: 'cmd-a', label: 'Command A' },
  { value: 'cmd-b', label: 'Command B' },
];

const makeIfEntry = (overrides?: Partial<IfStepEntry>): IfStepEntry => ({
  type: 'If',
  id: 'if-entry-1',
  stepId: 'if-1',
  condition: { type: 'imageVisible', imageId: 'img-1', minSimilarity: null },
  body: [],
  ...overrides,
});

describe('IfBlockHeader', () => {
  it('renders If badge and the same condition controls as a while loop', () => {
    render(
      <IfBlockHeader condition={{ type: 'imageVisible', imageId: 'my-img', minSimilarity: null }} />
    );
    expect(screen.getByTestId('if-type-badge')).toHaveTextContent('If');
    // Shared ConditionFields: NOT toggle, type select, imageVisible fields.
    expect(screen.getByTestId('if-condition-negate')).toBeInTheDocument();
    expect(screen.getByTestId('if-condition-type')).toHaveValue('imageVisible');
    expect(screen.getByTestId('if-condition-imageId')).toHaveValue('my-img');
    // No Max iterations field on if blocks.
    expect(screen.queryByTestId('loop-max-iterations')).not.toBeInTheDocument();
  });

  it('renders commandOutcome condition fields', () => {
    render(
      <IfBlockHeader condition={{ type: 'commandOutcome', stepRef: 'step-1', expectedState: 'success' }} />
    );
    expect(screen.getByTestId('if-condition-type')).toHaveValue('commandOutcome');
    expect(screen.getByTestId('if-condition-stepRef')).toHaveValue('step-1');
    expect(screen.getByTestId('if-condition-expectedState')).toHaveValue('success');
  });

  it('fires onConditionChange when negate is toggled', () => {
    const onConditionChange = jest.fn();
    render(
      <IfBlockHeader
        condition={{ type: 'imageVisible', imageId: 'img', minSimilarity: null }}
        onConditionChange={onConditionChange}
      />
    );
    fireEvent.click(screen.getByTestId('if-condition-negate'));
    expect(onConditionChange).toHaveBeenCalledWith({ type: 'imageVisible', imageId: 'img', minSimilarity: null, negate: true });
  });
});

describe('IfBlock', () => {
  it('renders then area with empty state and no else area by default', () => {
    render(<IfBlock ifEntry={makeIfEntry()} onChange={jest.fn()} onRemove={jest.fn()} />);
    expect(screen.getByTestId('if-block')).toBeInTheDocument();
    expect(screen.getByTestId('if-then-branch')).toBeInTheDocument();
    expect(screen.getByTestId('if-then-empty')).toBeInTheDocument();
    expect(screen.queryByTestId('if-else-branch')).not.toBeInTheDocument();
    expect(screen.getByTestId('if-add-else')).toBeInTheDocument();
  });

  it('adds a then step via Add step', () => {
    const onChange = jest.fn();
    render(<IfBlock ifEntry={makeIfEntry()} onChange={onChange} onRemove={jest.fn()} commandOptions={testCommands} />);
    fireEvent.click(screen.getByTestId('if-then-add-step'));
    expect(onChange).toHaveBeenCalledTimes(1);
    const updated = onChange.mock.calls[0][0] as IfStepEntry;
    expect(updated.body).toHaveLength(1);
    expect(updated.body[0].type).toBe('Action');
  });

  it('reveals the else area via Add else and removes it again', () => {
    const onChange = jest.fn();
    const { rerender } = render(<IfBlock ifEntry={makeIfEntry()} onChange={onChange} onRemove={jest.fn()} />);

    fireEvent.click(screen.getByTestId('if-add-else'));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ elseBody: [] }));

    // Re-render with the else branch present (parent applies the change).
    rerender(<IfBlock ifEntry={makeIfEntry({ elseBody: [] })} onChange={onChange} onRemove={jest.fn()} />);
    expect(screen.getByTestId('if-else-branch')).toBeInTheDocument();
    expect(screen.getByTestId('if-else-empty')).toBeInTheDocument();

    // Removing an empty else needs no confirmation.
    fireEvent.click(screen.getByTestId('if-remove-else'));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ elseBody: undefined }));
  });

  it('asks for confirmation before removing an else branch with steps', () => {
    const onChange = jest.fn();
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);
    const entry = makeIfEntry({
      elseBody: [{
        type: 'Action', id: 'e1', stepId: 'else-step-1', commandId: 'cmd-a',
        conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success',
      }],
    });
    render(<IfBlock ifEntry={entry} onChange={onChange} onRemove={jest.fn()} commandOptions={testCommands} />);

    fireEvent.click(screen.getByTestId('if-remove-else'));
    expect(confirmSpy).toHaveBeenCalled();
    expect(onChange).not.toHaveBeenCalled();
    confirmSpy.mockRestore();
  });

  it('renders command selects for branch action steps and updates commandId', () => {
    const onChange = jest.fn();
    const entry = makeIfEntry({
      body: [{
        type: 'Action', id: 't1', stepId: 'then-step-1', commandId: 'cmd-a',
        conditionType: 'none', imageId: '', minSimilarity: '', outcomeStepRef: '', expectedState: 'success',
      }],
    });
    render(<IfBlock ifEntry={entry} onChange={onChange} onRemove={jest.fn()} commandOptions={testCommands} />);

    const select = screen.getByTestId('if-then-command-select');
    expect(select).toHaveValue('cmd-a');
    fireEvent.change(select, { target: { value: 'cmd-b' } });
    const updated = onChange.mock.calls[0][0] as IfStepEntry;
    expect(updated.body[0]).toMatchObject({ commandId: 'cmd-b' });
  });

  it('offers Add break in branches only when the if block sits inside a loop body', () => {
    const { rerender } = render(<IfBlock ifEntry={makeIfEntry()} onChange={jest.fn()} onRemove={jest.fn()} />);
    expect(screen.queryByTestId('if-then-add-break')).not.toBeInTheDocument();

    rerender(<IfBlock ifEntry={makeIfEntry()} onChange={jest.fn()} onRemove={jest.fn()} allowBreakSteps />);
    expect(screen.getByTestId('if-then-add-break')).toBeInTheDocument();
  });

  it('never offers loop or nested if buttons inside branches', () => {
    render(<IfBlock ifEntry={makeIfEntry()} onChange={jest.fn()} onRemove={jest.fn()} allowBreakSteps />);
    expect(screen.queryByTestId('add-if-step')).not.toBeInTheDocument();
    expect(screen.queryByTestId('add-loop-step')).not.toBeInTheDocument();
  });

  it('fires onRemove from the Remove If button', () => {
    const onRemove = jest.fn();
    render(<IfBlock ifEntry={makeIfEntry()} onChange={jest.fn()} onRemove={onRemove} />);
    fireEvent.click(screen.getByTestId('if-remove'));
    expect(onRemove).toHaveBeenCalled();
  });
});
