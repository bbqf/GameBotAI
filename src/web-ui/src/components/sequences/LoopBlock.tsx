import React from 'react';
import type { LoopStepEntry, StepEntry } from '../../types/stepEntry';
import { LoopBlockHeader } from './LoopBlockHeader';
import { BreakStepRow } from './BreakStepRow';

export type LoopBlockProps = {
  loop: LoopStepEntry;
  onChange: (updated: LoopStepEntry) => void;
  onRemove: () => void;
  disabled?: boolean;
};

const makeId = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2);

const move = (items: StepEntry[], from: number, to: number): StepEntry[] => {
  const next = [...items];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
};

export const LoopBlock: React.FC<LoopBlockProps> = ({ loop, onChange, onRemove, disabled }) => {
  const body = loop.body;

  const handleBodyReorder = (from: number, to: number) => {
    if (to < 0 || to >= body.length) return;
    onChange({ ...loop, body: move(body, from, to) });
  };

  const handleBodyDelete = (index: number) => {
    onChange({ ...loop, body: body.filter((_, i) => i !== index) });
  };

  const handleAddBodyStep = () => {
    const newStep: StepEntry = {
      type: 'Action',
      id: makeId(),
      stepId: `body-step-${body.length + 1}`,
      commandId: '',
      conditionType: 'none',
      imageId: '',
      minSimilarity: '',
      outcomeStepRef: '',
      expectedState: 'success',
    };
    onChange({ ...loop, body: [...body, newStep] });
  };

  const handleAddBreakStep = () => {
    const newStep: StepEntry = {
      type: 'Break',
      id: makeId(),
      stepId: `break-${body.length + 1}`,
      breakCondition: undefined,
    };
    onChange({ ...loop, body: [...body, newStep] });
  };

  return (
    <div className="loop-block" data-testid="loop-block" style={{ borderLeft: '3px solid #4a90d9', paddingLeft: '12px', marginBottom: '8px' }}>
      <div className="loop-block__header-row">
        <LoopBlockHeader
          loopType={loop.loopType}
          count={loop.count}
          condition={loop.condition}
          maxIterations={loop.maxIterations}
        />
        <button type="button" onClick={onRemove} disabled={disabled} data-testid="loop-remove">Remove Loop</button>
      </div>

      <div className="loop-block__body" data-testid="loop-body">
        {body.length === 0 && (
          <div className="empty-state" data-testid="loop-body-empty">No body steps yet.</div>
        )}
        <ol>
          {body.map((step, index) => (
            <li key={step.id} className="loop-block__body-step" data-testid="loop-body-step">
              <div className="loop-block__body-step-content">
                {step.type === 'Break' ? (
                  <BreakStepRow
                    breakCondition={step.breakCondition}
                    onChange={(bc) => {
                      const updated = body.map((s, i) =>
                        i === index ? { ...s, breakCondition: bc } as StepEntry : s
                      );
                      onChange({ ...loop, body: updated });
                    }}
                    onRemove={() => handleBodyDelete(index)}
                    disabled={disabled}
                  />
                ) : (
                  <div className="loop-block__action-step" data-testid="loop-action-step">
                    <span>{step.type === 'Action' ? (step.commandId || step.stepId) : step.stepId}</span>
                  </div>
                )}
              </div>
              <div className="loop-block__body-step-controls">
                <button type="button" aria-label="Move up" onClick={() => handleBodyReorder(index, index - 1)} disabled={disabled || index === 0}>↑</button>
                <button type="button" aria-label="Move down" onClick={() => handleBodyReorder(index, index + 1)} disabled={disabled || index === body.length - 1}>↓</button>
                {step.type !== 'Break' && (
                  <button type="button" onClick={() => handleBodyDelete(index)} disabled={disabled}>Delete</button>
                )}
              </div>
            </li>
          ))}
        </ol>
      </div>

      <div className="loop-block__add-buttons">
        <button type="button" onClick={handleAddBodyStep} disabled={disabled} data-testid="add-body-step">
          Add step
        </button>
        <button type="button" onClick={handleAddBreakStep} disabled={disabled} data-testid="add-break-step">
          Add break
        </button>
      </div>
    </div>
  );
};
