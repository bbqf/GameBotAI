import React from 'react';
import type { SequenceStepCondition } from '../../types/sequenceFlow';
import { SimilarityInput } from './SimilarityInput';

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
            <label>
              <input
                type="checkbox"
                data-testid="break-negate-toggle"
                checked={breakCondition.negate ?? false}
                disabled={disabled}
                onChange={(e) => onChange({ ...breakCondition, negate: e.target.checked })}
              />
              NOT
            </label>
          </div>
          <div className="break-step-row__field">
            <label>Condition Type</label>
            <select
              data-testid="break-condition-type"
              value={breakCondition.type}
              disabled={disabled}
              onChange={(e) => {
                const newType = e.target.value;
                if (newType === 'imageVisible') {
                  onChange({ type: 'imageVisible', imageId: '', minSimilarity: null });
                } else {
                  onChange({ type: 'commandOutcome', stepRef: '', expectedState: 'success' });
                }
              }}
            >
              <option value="imageVisible">Image Visible</option>
              <option value="commandOutcome">Command Outcome</option>
            </select>
          </div>
          {breakCondition.type === 'imageVisible' && (
            <>
              <div className="break-step-row__field">
                <label>Image ID</label>
                <input
                  type="text"
                  data-testid="break-image-id"
                  value={breakCondition.imageId}
                  disabled={disabled}
                  placeholder="Enter image ID"
                  onChange={(e) => onChange({ ...breakCondition, imageId: e.target.value })}
                />
              </div>
              <div className="break-step-row__field">
                <label>Min Similarity</label>
                <SimilarityInput
                  data-testid="break-min-similarity"
                  value={breakCondition.minSimilarity}
                  disabled={disabled}
                  onChange={(v) => onChange({ ...breakCondition, minSimilarity: v })}
                />
              </div>
            </>
          )}
          {breakCondition.type === 'commandOutcome' && (
            <>
              <div className="break-step-row__field">
                <label>Step Ref</label>
                <input
                  type="text"
                  data-testid="break-step-ref"
                  value={breakCondition.stepRef}
                  disabled={disabled}
                  placeholder="Enter step reference"
                  onChange={(e) => onChange({ ...breakCondition, stepRef: e.target.value })}
                />
              </div>
              <div className="break-step-row__field">
                <label>Expected State</label>
                <select
                  data-testid="break-expected-state"
                  value={breakCondition.expectedState}
                  disabled={disabled}
                  onChange={(e) => onChange({ ...breakCondition, expectedState: e.target.value as 'success' | 'failed' | 'skipped' })}
                >
                  <option value="success">success</option>
                  <option value="failed">failed</option>
                  <option value="skipped">skipped</option>
                </select>
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
};
