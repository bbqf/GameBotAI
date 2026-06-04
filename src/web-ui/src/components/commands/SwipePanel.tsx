import React, { useState } from 'react';

export type SwipePanelValue = {
  startX: string;
  startY: string;
  endX: string;
  endY: string;
  durationMs?: string;
};

export type SwipePanelProps = {
  initialValue?: SwipePanelValue;
  onConfirm: (value: SwipePanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
};

const validateInteger = (value: string, label: string): string | null => {
  if (!value.trim()) return `${label} is required.`;
  if (!Number.isInteger(Number(value)) || isNaN(Number(value))) return `${label} must be an integer.`;
  return null;
};

const validateDuration = (value: string): string | null => {
  if (!value.trim()) return null;
  const n = parseInt(value, 10);
  if (isNaN(n) || n < 0) return 'Duration must be a non-negative integer.';
  return null;
};

export const SwipePanel: React.FC<SwipePanelProps> = ({ initialValue, onConfirm, onCancel, disabled }) => {
  const [startX, setStartX] = useState(initialValue?.startX ?? '');
  const [startY, setStartY] = useState(initialValue?.startY ?? '');
  const [endX, setEndX] = useState(initialValue?.endX ?? '');
  const [endY, setEndY] = useState(initialValue?.endY ?? '');
  const [durationMs, setDurationMs] = useState(initialValue?.durationMs ?? '');
  const [attempted, setAttempted] = useState(false);

  const startXError = validateInteger(startX, 'Start X');
  const startYError = validateInteger(startY, 'Start Y');
  const endXError = validateInteger(endX, 'End X');
  const endYError = validateInteger(endY, 'End Y');
  const durationError = validateDuration(durationMs);
  const hasErrors = Boolean(startXError || startYError || endXError || endYError || durationError);

  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (hasErrors) return;
    onConfirm({
      startX: startX.trim(),
      startY: startY.trim(),
      endX: endX.trim(),
      endY: endY.trim(),
      durationMs: durationMs.trim() || undefined,
    });
  };

  return (
    <div className="action-panel action-panel--swipe">
      <div className="field">
        <label htmlFor="swipe-panel-start-x">Start X *</label>
        <input
          id="swipe-panel-start-x"
          type="number"
          value={startX}
          onChange={(e) => setStartX(e.target.value)}
          disabled={disabled}
          aria-invalid={attempted && Boolean(startXError)}
          aria-describedby={attempted && startXError ? 'swipe-panel-start-x-error' : undefined}
        />
        {attempted && startXError && (
          <div id="swipe-panel-start-x-error" className="field-error" role="alert">{startXError}</div>
        )}
      </div>
      <div className="field">
        <label htmlFor="swipe-panel-start-y">Start Y *</label>
        <input
          id="swipe-panel-start-y"
          type="number"
          value={startY}
          onChange={(e) => setStartY(e.target.value)}
          disabled={disabled}
          aria-invalid={attempted && Boolean(startYError)}
          aria-describedby={attempted && startYError ? 'swipe-panel-start-y-error' : undefined}
        />
        {attempted && startYError && (
          <div id="swipe-panel-start-y-error" className="field-error" role="alert">{startYError}</div>
        )}
      </div>
      <div className="field">
        <label htmlFor="swipe-panel-end-x">End X *</label>
        <input
          id="swipe-panel-end-x"
          type="number"
          value={endX}
          onChange={(e) => setEndX(e.target.value)}
          disabled={disabled}
          aria-invalid={attempted && Boolean(endXError)}
          aria-describedby={attempted && endXError ? 'swipe-panel-end-x-error' : undefined}
        />
        {attempted && endXError && (
          <div id="swipe-panel-end-x-error" className="field-error" role="alert">{endXError}</div>
        )}
      </div>
      <div className="field">
        <label htmlFor="swipe-panel-end-y">End Y *</label>
        <input
          id="swipe-panel-end-y"
          type="number"
          value={endY}
          onChange={(e) => setEndY(e.target.value)}
          disabled={disabled}
          aria-invalid={attempted && Boolean(endYError)}
          aria-describedby={attempted && endYError ? 'swipe-panel-end-y-error' : undefined}
        />
        {attempted && endYError && (
          <div id="swipe-panel-end-y-error" className="field-error" role="alert">{endYError}</div>
        )}
      </div>
      <div className="field">
        <label htmlFor="swipe-panel-duration">Duration (ms)</label>
        <input
          id="swipe-panel-duration"
          type="number"
          min="0"
          value={durationMs}
          onChange={(e) => setDurationMs(e.target.value)}
          disabled={disabled}
        />
        {attempted && durationError && (
          <div className="field-error" role="alert">{durationError}</div>
        )}
      </div>
      <div className="action-panel__controls">
        <button type="button" onClick={handleConfirm} disabled={disabled}>
          {buttonLabel}
        </button>
        <button type="button" onClick={onCancel} disabled={disabled}>
          Cancel
        </button>
      </div>
    </div>
  );
};
