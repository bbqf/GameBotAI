import React, { useState } from 'react';
import { ParameterSourceControl } from '../parameters/ParameterSourceControl';
import { useFieldTemplates } from '../parameters/useFieldTemplates';
import type { ParameterDeclaration } from '../parameters/types';

export type SwipePanelValue = {
  startX: string;
  startY: string;
  endX: string;
  endY: string;
  durationMs?: string;
  /** Placeholders for step fields (feature 169). Includes keys that this panel has no control for. */
  fieldTemplates?: Record<string, string>;
};

export type SwipePanelProps = {
  initialValue?: SwipePanelValue;
  onConfirm: (value: SwipePanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
  /** Parameters that the command declares (feature 169). */
  parameters?: ParameterDeclaration[];
  /** Placeholders of the step that is in edit. */
  fieldTemplates?: Record<string, string>;
  /** Service messages by field path, for this step only. */
  fieldErrors?: Record<string, string>;
};

const P = {
  startX: 'swipe.startX',
  startY: 'swipe.startY',
  endX: 'swipe.endX',
  endY: 'swipe.endY',
  durationMs: 'swipe.durationMs',
} as const;

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

export const SwipePanel: React.FC<SwipePanelProps> = ({
  initialValue,
  onConfirm,
  onCancel,
  disabled,
  parameters,
  fieldTemplates,
  fieldErrors,
}) => {
  const templates = useFieldTemplates(fieldTemplates);
  const [startX, setStartX] = useState(initialValue?.startX ?? '');
  const [startY, setStartY] = useState(initialValue?.startY ?? '');
  const [endX, setEndX] = useState(initialValue?.endX ?? '');
  const [endY, setEndY] = useState(initialValue?.endY ?? '');
  const [durationMs, setDurationMs] = useState(initialValue?.durationMs ?? '');
  const [attempted, setAttempted] = useState(false);

  // A field with a placeholder is valid: the service checks the value that the parameter gives.
  const startXError = templates.get(P.startX) ? null : validateInteger(startX, 'Start X');
  const startYError = templates.get(P.startY) ? null : validateInteger(startY, 'Start Y');
  const endXError = templates.get(P.endX) ? null : validateInteger(endX, 'End X');
  const endYError = templates.get(P.endY) ? null : validateInteger(endY, 'End Y');
  const durationError = templates.get(P.durationMs) ? null : validateDuration(durationMs);
  const hasErrors = Boolean(startXError || startYError || endXError || endYError || durationError);

  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (hasErrors) return;
    // The kept plain value goes to the service. It is 0 for an empty required whole-number field.
    const kept = (value: string) => value.trim() || '0';
    onConfirm({
      startX: kept(startX),
      startY: kept(startY),
      endX: kept(endX),
      endY: kept(endY),
      durationMs: durationMs.trim() || undefined,
      fieldTemplates: templates.result(),
    });
  };

  const renderField = (
    path: string,
    id: string,
    label: string,
    value: string,
    setValue: (v: string) => void,
    error: string | null,
    extra?: { min?: string },
  ) => (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <ParameterSourceControl
        path={path}
        label={label.replace(' *', '')}
        parameters={parameters ?? []}
        template={templates.get(path)}
        onChange={(t) => templates.set(path, t)}
        disabled={disabled}
        error={fieldErrors?.[path]}
      >
        <input
          id={id}
          type="number"
          min={extra?.min}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          disabled={disabled}
          aria-invalid={attempted && Boolean(error)}
          aria-describedby={attempted && error ? `${id}-error` : undefined}
        />
      </ParameterSourceControl>
      {attempted && error && (
        <div id={`${id}-error`} className="field-error" role="alert">{error}</div>
      )}
    </div>
  );

  return (
    <div className="action-panel action-panel--swipe">
      {renderField(P.startX, 'swipe-panel-start-x', 'Start X *', startX, setStartX, startXError)}
      {renderField(P.startY, 'swipe-panel-start-y', 'Start Y *', startY, setStartY, startYError)}
      {renderField(P.endX, 'swipe-panel-end-x', 'End X *', endX, setEndX, endXError)}
      {renderField(P.endY, 'swipe-panel-end-y', 'End Y *', endY, setEndY, endYError)}
      {renderField(P.durationMs, 'swipe-panel-duration', 'Duration (ms)', durationMs, setDurationMs, durationError, { min: '0' })}
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
