import React from 'react';
import type { SequenceStepCondition } from '../../types/sequenceFlow';
import { SimilarityInput } from './SimilarityInput';
import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown';

export type ConditionFieldsProps = {
  condition?: SequenceStepCondition;
  disabled?: boolean;
  onConditionChange?: (condition: SequenceStepCondition) => void;
  /** Prefix for data-testid attributes; defaults to the loop editor's historical ids. */
  testIdPrefix?: string;
};

/**
 * Shared condition editor used by while/repeat-until loop headers and if-block headers,
 * guaranteeing identical condition controls across both block types (feature 067, FR-008).
 */
export const ConditionFields: React.FC<ConditionFieldsProps> = ({
  condition, disabled, onConditionChange, testIdPrefix = 'loop-condition',
}) => (
  <div className="loop-block-header__condition-fields">
    <label className="loop-block-header__negate-toggle">
      <input
        type="checkbox"
        data-testid={`${testIdPrefix}-negate`}
        checked={condition?.negate ?? false}
        disabled={disabled}
        onChange={(e) => {
          if (condition) onConditionChange?.({ ...condition, negate: e.target.checked });
        }}
      />
      NOT
    </label>
    <select
      data-testid={`${testIdPrefix}-type`}
      value={condition?.type ?? 'imageVisible'}
      disabled={disabled}
      onChange={(e) => {
        const type = e.target.value as 'imageVisible' | 'commandOutcome';
        if (type === 'imageVisible') {
          onConditionChange?.({ type: 'imageVisible', imageId: condition?.type === 'imageVisible' ? condition.imageId : '', minSimilarity: null });
        } else {
          onConditionChange?.({ type: 'commandOutcome', stepRef: condition?.type === 'commandOutcome' ? condition.stepRef : '', expectedState: condition?.type === 'commandOutcome' ? condition.expectedState : 'success' });
        }
      }}
    >
      <option value="imageVisible">imageVisible</option>
      <option value="commandOutcome">commandOutcome</option>
    </select>

    {condition?.type === 'imageVisible' && (
      <>
        <ImageSelectorDropdown
          data-testid={`${testIdPrefix}-imageId`}
          value={condition.imageId}
          onChange={(id) => onConditionChange?.({ ...condition, imageId: id })}
          disabled={disabled}
        />
        <SimilarityInput
          data-testid={`${testIdPrefix}-minSimilarity`}
          value={condition.minSimilarity}
          disabled={disabled}
          onChange={(v) => onConditionChange?.({ ...condition, minSimilarity: v })}
          style={{ width: '90px' }}
        />
      </>
    )}
    {condition?.type === 'commandOutcome' && (
      <>
        <input
          data-testid={`${testIdPrefix}-stepRef`}
          placeholder="Step ref"
          value={condition.stepRef}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, stepRef: e.target.value })}
        />
        <select
          data-testid={`${testIdPrefix}-expectedState`}
          value={condition.expectedState}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, expectedState: e.target.value as 'success' | 'failed' | 'skipped' })}
        >
          <option value="success">success</option>
          <option value="failed">failed</option>
          <option value="skipped">skipped</option>
        </select>
      </>
    )}
  </div>
);
