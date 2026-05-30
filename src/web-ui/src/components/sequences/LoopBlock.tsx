import React from 'react';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import type { LoopStepEntry, StepEntry, ActionStepEntry } from '../../types/stepEntry';
import type { SequenceStepCondition } from '../../types/sequenceFlow';
import { LoopBlockHeader } from './LoopBlockHeader';
import { BreakStepRow } from './BreakStepRow';
import { SortableStepItem } from '../SortableStepItem';
import { DropIndicator, dropIndicatorBefore } from '../DropIndicator';

export type CommandOption = { value: string; label: string };

export type LoopBlockProps = {
  loop: LoopStepEntry;
  onChange: (updated: LoopStepEntry) => void;
  onRemove: () => void;
  commandOptions?: CommandOption[];
  disabled?: boolean;
  isDropInvalid?: boolean;
  activeBodyStepId?: string | null;
  overBodyStepId?: string | null;
};

const makeId = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2);

export const LoopBlock: React.FC<LoopBlockProps> = ({
  loop, onChange, onRemove, commandOptions = [], disabled, isDropInvalid,
  activeBodyStepId, overBodyStepId,
}) => {
  const body = loop.body;
  const bodyIds = body.map((s) => s.id);
  const indicatorBefore = dropIndicatorBefore(bodyIds, activeBodyStepId ?? null, overBodyStepId ?? null);

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
          disabled={disabled}
          onCountChange={(count) => onChange({ ...loop, count })}
          onMaxIterationsChange={(maxIterations) => onChange({ ...loop, maxIterations })}
          onConditionChange={(condition: SequenceStepCondition) => onChange({ ...loop, condition })}
        />
        <button type="button" onClick={onRemove} disabled={disabled} data-testid="loop-remove">Remove Loop</button>
      </div>

      <div className={`loop-block__body${isDropInvalid ? ' loop-block--drop-invalid' : ''}`} data-testid="loop-body">
        {body.length === 0 && (
          <div className="empty-state" data-testid="loop-body-empty">No body steps yet.</div>
        )}
        <SortableContext items={bodyIds} strategy={verticalListSortingStrategy}>
          {body.map((step, index) => (
            <React.Fragment key={step.id}>
              {indicatorBefore === index && <DropIndicator />}
              <div className="loop-block__body-step" data-testid="loop-body-step">
                <SortableStepItem id={step.id} scopeId={loop.id} disabled={disabled}>
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
                        <select
                          data-testid="loop-body-command-select"
                          value={(step as ActionStepEntry).commandId}
                          disabled={disabled}
                          onChange={(e) => {
                            const updated = body.map((s, i) =>
                              i === index ? { ...s, commandId: e.target.value, commandReference: undefined } as StepEntry : s
                            );
                            onChange({ ...loop, body: updated });
                          }}
                        >
                          <option value="">Select command…</option>
                          {commandOptions.map((opt) => (
                            <option key={opt.value} value={opt.value}>{opt.label}</option>
                          ))}
                        </select>
                      </div>
                    )}
                  </div>
                  {step.type !== 'Break' && (
                    <div className="loop-block__body-step-controls">
                      <button type="button" onClick={() => handleBodyDelete(index)} disabled={disabled}>Delete</button>
                    </div>
                  )}
                </SortableStepItem>
              </div>
            </React.Fragment>
          ))}
          {indicatorBefore === body.length && <DropIndicator />}
        </SortableContext>
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
