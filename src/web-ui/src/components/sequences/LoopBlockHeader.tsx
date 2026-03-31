import React from 'react';
import type { SequenceStepCondition } from '../../types/sequenceFlow';

export type LoopBlockHeaderProps = {
  loopType: 'count' | 'while' | 'repeatUntil';
  count?: number;
  condition?: SequenceStepCondition;
  maxIterations?: number;
};

const loopTypeBadge: Record<string, string> = {
  count: 'Count',
  while: 'While',
  repeatUntil: 'Repeat‑Until',
};

const conditionSummary = (condition?: SequenceStepCondition): string => {
  if (!condition) return '';
  if (condition.type === 'imageVisible') return `imageVisible "${condition.imageId}"`;
  if (condition.type === 'commandOutcome') return `commandOutcome ${condition.stepRef} = ${condition.expectedState}`;
  return '';
};

export const LoopBlockHeader: React.FC<LoopBlockHeaderProps> = ({ loopType, count, condition, maxIterations }) => {
  const badge = loopTypeBadge[loopType] ?? loopType;

  let summary = '';
  if (loopType === 'count') {
    summary = `× ${count ?? 0}`;
  } else {
    summary = conditionSummary(condition);
  }

  return (
    <div className="loop-block-header" data-testid="loop-block-header">
      <span className="loop-block-header__badge" data-testid="loop-type-badge">{badge}</span>
      <span className="loop-block-header__summary" data-testid="loop-summary">{summary}</span>
      {maxIterations != null && (
        <span className="loop-block-header__limit" data-testid="loop-max-iterations">max {maxIterations}</span>
      )}
      {(loopType === 'count' || loopType === 'while') && (
        <span className="loop-block-header__hint" data-testid="loop-iteration-hint">{'{{iteration}}'}</span>
      )}
    </div>
  );
};
