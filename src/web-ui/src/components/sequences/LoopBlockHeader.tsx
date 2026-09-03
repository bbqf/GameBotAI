import React from 'react';
import type { SequenceStepCondition } from '../../types/sequenceFlow';
import { ConditionFields } from './ConditionFields';

export type LoopBlockHeaderProps = {
  loopType: 'count' | 'while' | 'repeatUntil';
  count?: number;
  condition?: SequenceStepCondition;
  maxIterations?: number;
  exitOnMaxIterations?: boolean;
  disabled?: boolean;
  onCountChange?: (count: number) => void;
  onMaxIterationsChange?: (maxIterations: number | undefined) => void;
  onExitOnMaxIterationsChange?: (exitOnMaxIterations: boolean) => void;
  onConditionChange?: (condition: SequenceStepCondition) => void;
};

const loopTypeBadge: Record<string, string> = {
  count: 'Count',
  while: 'While',
  repeatUntil: 'Repeat‑Until',
};

export const LoopBlockHeader: React.FC<LoopBlockHeaderProps> = ({
  loopType, count, condition, maxIterations, exitOnMaxIterations, disabled,
  onCountChange, onMaxIterationsChange, onExitOnMaxIterationsChange, onConditionChange,
}) => {
  const badge = loopTypeBadge[loopType] ?? loopType;

  return (
    <div className="loop-block-header" data-testid="loop-block-header">
      <span className="loop-block-header__badge" data-testid="loop-type-badge">{badge}</span>

      {loopType === 'count' ? (
        <label className="loop-block-header__count-field">
          ×{' '}
          <input
            type="number"
            min={0}
            data-testid="loop-count-input"
            value={count ?? 0}
            disabled={disabled}
            onChange={(e) => onCountChange?.(Math.max(0, parseInt(e.target.value, 10) || 0))}
            style={{ width: '60px' }}
          />
        </label>
      ) : (
        <ConditionFields
          condition={condition}
          disabled={disabled}
          onConditionChange={onConditionChange}
        />
      )}

      {/* A count loop is bounded by its count; the safety cap (Max) only applies to while/repeat-until. */}
      {loopType !== 'count' && (
        <label className="loop-block-header__limit-field">
          Max:{' '}
          <input
            type="number"
            min={1}
            data-testid="loop-max-iterations"
            placeholder="∞"
            value={maxIterations ?? ''}
            disabled={disabled}
            onChange={(e) => {
              const val = parseInt(e.target.value, 10);
              onMaxIterationsChange?.(isNaN(val) ? undefined : Math.max(1, val));
            }}
            style={{ width: '60px' }}
          />
        </label>
      )}

      {/* Reaching the cap normally fails the step and stops the sequence. A best-effort guard —
          "press BACK until the city is on screen" — wants the opposite: give up, carry on. */}
      {loopType !== 'count' && (
        <label className="loop-block-header__limit-field" title="Reaching Max ends the loop instead of failing the sequence">
          <input
            type="checkbox"
            data-testid="loop-exit-on-max"
            checked={exitOnMaxIterations ?? false}
            disabled={disabled}
            onChange={(e) => onExitOnMaxIterationsChange?.(e.target.checked)}
          />{' '}
          Give up at Max
        </label>
      )}
    </div>
  );
};
