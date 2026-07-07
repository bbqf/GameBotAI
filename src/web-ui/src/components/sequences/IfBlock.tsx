import React from 'react';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import type { IfStepEntry, StepEntry, ActionStepEntry } from '../../types/stepEntry';
import type { SequenceStepCondition } from '../../types/sequenceFlow';
import { IfBlockHeader } from './IfBlockHeader';
import { BreakStepRow } from './BreakStepRow';
import { SortableStepItem } from '../SortableStepItem';
import { DropIndicator, dropIndicatorBefore } from '../DropIndicator';

export type CommandOption = { value: string; label: string };

export type IfBlockProps = {
  ifEntry: IfStepEntry;
  onChange: (updated: IfStepEntry) => void;
  onRemove: () => void;
  commandOptions?: CommandOption[];
  disabled?: boolean;
  /** True when the if block sits inside a loop body; enables break steps in branches. */
  allowBreakSteps?: boolean;
  isDropInvalid?: boolean;
  activeBodyStepId?: string | null;
  overBodyStepId?: string | null;
};

const makeId = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2);

type BranchName = 'then' | 'else';

/**
 * If block editor (feature 067): visually parallel to LoopBlock, with a then area that is
 * always visible and an else area revealed on demand via "Add else". Branch contents follow
 * loop-body rules (no nested loops or ifs; breaks only when the if sits inside a loop body).
 */
export const IfBlock: React.FC<IfBlockProps> = ({
  ifEntry, onChange, onRemove, commandOptions = [], disabled, allowBreakSteps,
  isDropInvalid, activeBodyStepId, overBodyStepId,
}) => {
  const updateBranch = (branch: BranchName, steps: StepEntry[]) => {
    if (branch === 'then') onChange({ ...ifEntry, body: steps });
    else onChange({ ...ifEntry, elseBody: steps });
  };

  const addActionStep = (branch: BranchName, steps: StepEntry[]) => {
    const newStep: StepEntry = {
      type: 'Action',
      id: makeId(),
      stepId: `${branch}-step-${steps.length + 1}`,
      commandId: '',
      conditionType: 'none',
      imageId: '',
      minSimilarity: '',
      outcomeStepRef: '',
      expectedState: 'success',
    };
    updateBranch(branch, [...steps, newStep]);
  };

  const addBreakStep = (branch: BranchName, steps: StepEntry[]) => {
    const newStep: StepEntry = {
      type: 'Break',
      id: makeId(),
      stepId: `${branch}-break-${steps.length + 1}`,
      breakCondition: undefined,
    };
    updateBranch(branch, [...steps, newStep]);
  };

  const renderBranch = (branch: BranchName, steps: StepEntry[]) => {
    const scopeId = `${ifEntry.id}:${branch}`;
    const stepIds = steps.map((s) => s.id);
    const activeInBranch = steps.some((s) => s.id === activeBodyStepId) ? activeBodyStepId ?? null : null;
    const overInBranch = steps.some((s) => s.id === overBodyStepId) ? overBodyStepId ?? null : null;
    const indicatorBefore = dropIndicatorBefore(stepIds, activeInBranch, overInBranch);

    return (
      <div className={`if-block__branch-body${isDropInvalid ? ' loop-block--drop-invalid' : ''}`} data-testid={`if-${branch}-body`}>
        {steps.length === 0 && (
          <div className="empty-state" data-testid={`if-${branch}-empty`}>No {branch} steps yet.</div>
        )}
        <SortableContext items={stepIds} strategy={verticalListSortingStrategy}>
          {steps.map((step, index) => (
            <React.Fragment key={step.id}>
              {indicatorBefore === index && <DropIndicator />}
              <div className="loop-block__body-step" data-testid={`if-${branch}-step`}>
                <SortableStepItem id={step.id} scopeId={scopeId} disabled={disabled}>
                  <div className="loop-block__body-step-content">
                    {step.type === 'Break' ? (
                      <BreakStepRow
                        breakCondition={step.breakCondition}
                        onChange={(bc) => {
                          const updated = steps.map((s, i) =>
                            i === index ? { ...s, breakCondition: bc } as StepEntry : s
                          );
                          updateBranch(branch, updated);
                        }}
                        onRemove={() => updateBranch(branch, steps.filter((_, i) => i !== index))}
                        disabled={disabled}
                      />
                    ) : (
                      <div className="loop-block__action-step" data-testid={`if-${branch}-action-step`}>
                        <select
                          data-testid={`if-${branch}-command-select`}
                          value={(step as ActionStepEntry).commandId}
                          disabled={disabled}
                          onChange={(e) => {
                            const updated = steps.map((s, i) =>
                              i === index ? { ...s, commandId: e.target.value, commandReference: undefined } as StepEntry : s
                            );
                            updateBranch(branch, updated);
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
                      <button type="button" onClick={() => updateBranch(branch, steps.filter((_, i) => i !== index))} disabled={disabled}>Delete</button>
                    </div>
                  )}
                </SortableStepItem>
              </div>
            </React.Fragment>
          ))}
          {indicatorBefore === steps.length && <DropIndicator />}
        </SortableContext>
        <div className="loop-block__add-buttons">
          <button type="button" onClick={() => addActionStep(branch, steps)} disabled={disabled} data-testid={`if-${branch}-add-step`}>
            Add step
          </button>
          {allowBreakSteps && (
            <button type="button" onClick={() => addBreakStep(branch, steps)} disabled={disabled} data-testid={`if-${branch}-add-break`}>
              Add break
            </button>
          )}
        </div>
      </div>
    );
  };

  const handleAddElse = () => onChange({ ...ifEntry, elseBody: [] });

  const handleRemoveElse = () => {
    const hasSteps = (ifEntry.elseBody?.length ?? 0) > 0;
    if (hasSteps && typeof window !== 'undefined' && !window.confirm('Remove the else branch and its steps?')) {
      return;
    }
    onChange({ ...ifEntry, elseBody: undefined });
  };

  return (
    <div className="if-block" data-testid="if-block" style={{ borderLeft: '3px solid #7a5cc7', paddingLeft: '12px', marginBottom: '8px' }}>
      <div className="loop-block__header-row">
        <IfBlockHeader
          condition={ifEntry.condition}
          disabled={disabled}
          onConditionChange={(condition: SequenceStepCondition) => onChange({ ...ifEntry, condition })}
        />
        <button type="button" onClick={onRemove} disabled={disabled} data-testid="if-remove">Remove If</button>
      </div>

      <div className="if-block__branch" data-testid="if-then-branch">
        <div className="if-block__branch-label">Then</div>
        {renderBranch('then', ifEntry.body)}
      </div>

      {ifEntry.elseBody === undefined ? (
        <div className="if-block__add-else">
          <button type="button" onClick={handleAddElse} disabled={disabled} data-testid="if-add-else">
            Add else
          </button>
        </div>
      ) : (
        <div className="if-block__branch" data-testid="if-else-branch">
          <div className="if-block__branch-label">
            Else{' '}
            <button type="button" onClick={handleRemoveElse} disabled={disabled} data-testid="if-remove-else">
              Remove else
            </button>
          </div>
          {renderBranch('else', ifEntry.elseBody)}
        </div>
      )}
    </div>
  );
};
