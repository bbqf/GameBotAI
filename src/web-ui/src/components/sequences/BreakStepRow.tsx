import React from 'react';
import type { SequenceStepCondition } from '../../types/sequenceFlow';

export type BreakStepRowProps = {
  breakCondition?: SequenceStepCondition;
  onChange: (breakCondition: SequenceStepCondition | undefined) => void;
  onRemove: () => void;
  disabled?: boolean;
};

export const BreakStepRow: React.FC<BreakStepRowProps> = ({ breakCondition, onChange, onRemove, disabled }) => {
  const isUnconditional = breakCondition == null;

  return (
    <div className="break-step-row" data-testid="break-step-row">
      <div className="break-step-row__header">
        <span className="break-step-row__badge">Break</span>
        <label className="break-step-row__toggle">
          <input
            type="checkbox"
            data-testid="always-break-toggle"
            checked={isUnconditional}
            disabled={disabled}
            onChange={(e) => {
              if (e.target.checked) {
                onChange(undefined);
              } else {
                onChange({ type: 'imageVisible', imageId: '', minSimilarity: null });
              }
            }}
          />
          Always break
        </label>
        <button type="button" onClick={onRemove} disabled={disabled} data-testid="break-remove">Remove</button>
      </div>
      {!isUnconditional && (
        <div className="break-step-row__condition" data-testid="break-condition-editor">
          <div className="break-step-row__field">
            <label>Condition Type</label>
            <span>{breakCondition.type}</span>
          </div>
          {breakCondition.type === 'imageVisible' && (
            <div className="break-step-row__field">
              <label>Image ID</label>
              <span>{breakCondition.imageId}</span>
            </div>
          )}
          {breakCondition.type === 'commandOutcome' && (
            <>
              <div className="break-step-row__field">
                <label>Step Ref</label>
                <span>{breakCondition.stepRef}</span>
              </div>
              <div className="break-step-row__field">
                <label>Expected State</label>
                <span>{breakCondition.expectedState}</span>
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
};
