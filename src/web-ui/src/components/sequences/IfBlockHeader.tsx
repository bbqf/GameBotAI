import React from 'react';
import type { SequenceStepCondition } from '../../types/sequenceFlow';
import { ConditionFields } from './ConditionFields';

export type IfBlockHeaderProps = {
  condition?: SequenceStepCondition;
  disabled?: boolean;
  onConditionChange?: (condition: SequenceStepCondition) => void;
};

/**
 * Header for an if block: "If" badge plus the same condition editor a while loop uses
 * (feature 067). No Max field — an if block executes its branch at most once.
 */
export const IfBlockHeader: React.FC<IfBlockHeaderProps> = ({ condition, disabled, onConditionChange }) => (
  <div className="loop-block-header" data-testid="if-block-header">
    <span className="loop-block-header__badge" data-testid="if-type-badge">If</span>
    <ConditionFields
      condition={condition}
      disabled={disabled}
      onConditionChange={onConditionChange}
      testIdPrefix="if-condition"
    />
  </div>
);
