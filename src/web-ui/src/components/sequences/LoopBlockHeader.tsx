import React from 'react';
import type { SequenceStepCondition } from '../../types/sequenceFlow';

export type LoopBlockHeaderProps = {
  loopType: 'count' | 'while' | 'repeatUntil';
  count?: number;
  condition?: SequenceStepCondition;
  maxIterations?: number;
  disabled?: boolean;
  onCountChange?: (count: number) => void;
  onMaxIterationsChange?: (maxIterations: number | undefined) => void;
  onConditionChange?: (condition: SequenceStepCondition) => void;
};

const loopTypeBadge: Record<string, string> = {
  count: 'Count',
  while: 'While',
  repeatUntil: 'Repeat‑Until',
};

export const LoopBlockHeader: React.FC<LoopBlockHeaderProps> = ({
  loopType, count, condition, maxIterations, disabled,
  onCountChange, onMaxIterationsChange, onConditionChange,
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
        <div className="loop-block-header__condition-fields">
          <label className="loop-block-header__negate-toggle">
            <input
              type="checkbox"
              data-testid="loop-condition-negate"
              checked={condition?.negate ?? false}
              disabled={disabled}
              onChange={(e) => {
                if (condition) onConditionChange?.({ ...condition, negate: e.target.checked });
              }}
            />
            NOT
          </label>
          <select
            data-testid="loop-condition-type"
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
              <input
                data-testid="loop-condition-imageId"
                placeholder="Image ID"
                value={condition.imageId}
                disabled={disabled}
                onChange={(e) => onConditionChange?.({ ...condition, imageId: e.target.value })}
              />
              <input
                type="text"
                inputMode="decimal"
                data-testid="loop-condition-minSimilarity"
                placeholder="0–1 (default: 0.85)"
                value={condition.minSimilarity ?? ''}
                disabled={disabled}
                onChange={(e) => {
                  const raw = e.target.value;
                  if (raw === '' || raw === '.' || raw === '0.') {
                    onConditionChange?.({ ...condition, minSimilarity: raw === '' ? null : condition.minSimilarity });
                    return;
                  }
                  const num = Number(raw);
                  if (!isNaN(num) && num >= 0 && num <= 1) {
                    onConditionChange?.({ ...condition, minSimilarity: num });
                  }
                }}
                style={{ width: '90px' }}
              />
            </>
          )}
          {condition?.type === 'commandOutcome' && (
            <>
              <input
                data-testid="loop-condition-stepRef"
                placeholder="Step ref"
                value={condition.stepRef}
                disabled={disabled}
                onChange={(e) => onConditionChange?.({ ...condition, stepRef: e.target.value })}
              />
              <select
                data-testid="loop-condition-expectedState"
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
      )}

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

      {(loopType === 'count' || loopType === 'while') && (
        <span className="loop-block-header__hint" data-testid="loop-iteration-hint">{'{{iteration}}'}</span>
      )}
    </div>
  );
};
