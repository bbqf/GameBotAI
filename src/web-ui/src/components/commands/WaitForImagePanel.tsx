import React, { useState } from 'react';
import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown';
import { ParameterSourceControl } from '../parameters/ParameterSourceControl';
import { useFieldTemplates } from '../parameters/useFieldTemplates';
import type { ParameterDeclaration } from '../parameters/types';

export type WaitForImagePanelValue = {
  timeoutMs: string;
  referenceImageId?: string;
  confidence?: string;
  /** Placeholders for step fields (feature 169). Includes keys that this panel has no control for. */
  fieldTemplates?: Record<string, string>;
};

export type WaitForImagePanelProps = {
  initialValue?: {
    timeoutMs?: string;
    referenceImageId?: string;
    confidence?: string;
  };
  onConfirm: (value: WaitForImagePanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
  /** Parameters that the command declares (feature 169). */
  parameters?: ParameterDeclaration[];
  /** Placeholders of the step that is in edit. */
  fieldTemplates?: Record<string, string>;
  /** Service messages by field path, for this step only. */
  fieldErrors?: Record<string, string>;
};

const TIMEOUT_PATH = 'waitForImage.timeoutMs';
const IMAGE_PATH = 'waitForImage.detectionTarget.referenceImageId';

const validateTimeoutMs = (value: string): string | null => {
  if (!value.trim()) return 'Timeout must be a non-negative whole number (ms).';
  if (!/^\d+$/.test(value.trim())) return 'Timeout must be a non-negative whole number (ms).';
  return null;
};

const validateConfidence = (confidence: string): string | null => {
  if (!confidence.trim()) return null;
  const n = parseFloat(confidence);
  if (isNaN(n) || n < 0 || n > 1) return 'Confidence must be a number between 0 and 1.';
  return null;
};

export const WaitForImagePanel: React.FC<WaitForImagePanelProps> = ({
  initialValue,
  onConfirm,
  onCancel,
  disabled,
  parameters,
  fieldTemplates,
  fieldErrors,
}) => {
  const templates = useFieldTemplates(fieldTemplates);
  const [timeoutMs, setTimeoutMs] = useState(initialValue?.timeoutMs ?? '1000');
  const [referenceImageId, setReferenceImageId] = useState(initialValue?.referenceImageId ?? '');
  const [confidence, setConfidence] = useState(initialValue?.confidence ?? '');
  const [attempted, setAttempted] = useState(false);

  const timeoutError = templates.get(TIMEOUT_PATH) ? null : validateTimeoutMs(timeoutMs);
  const confidenceError = validateConfidence(confidence);
  const hasErrors = Boolean(timeoutError || confidenceError);

  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (hasErrors) return;
    onConfirm({
      // The kept plain value goes to the service. It is 0 for an empty required whole-number field.
      timeoutMs: timeoutMs.trim() || '0',
      referenceImageId: referenceImageId.trim() || undefined,
      confidence: confidence.trim() || undefined,
      fieldTemplates: templates.result(),
    });
  };

  return (
    <div className="action-panel action-panel--wait-for-image">
      <div className="field">
        <label htmlFor="wfi-panel-timeout">Timeout (ms) *</label>
        <ParameterSourceControl
          path={TIMEOUT_PATH}
          label="Timeout"
          parameters={parameters ?? []}
          template={templates.get(TIMEOUT_PATH)}
          onChange={(t) => templates.set(TIMEOUT_PATH, t)}
          disabled={disabled}
          error={fieldErrors?.[TIMEOUT_PATH]}
        >
          <input
            id="wfi-panel-timeout"
            type="number"
            min="0"
            value={timeoutMs}
            onChange={(e) => setTimeoutMs(e.target.value)}
            disabled={disabled}
          />
        </ParameterSourceControl>
        {attempted && timeoutError && <div className="field-error" role="alert">{timeoutError}</div>}
      </div>
      <div className="field">
        <ParameterSourceControl
          path={IMAGE_PATH}
          label="Reference image"
          parameters={parameters ?? []}
          template={templates.get(IMAGE_PATH)}
          onChange={(t) => templates.set(IMAGE_PATH, t)}
          disabled={disabled}
          error={fieldErrors?.[IMAGE_PATH]}
        >
          <ImageSelectorDropdown
            id="wfi-panel-image"
            label="Reference image (optional)"
            value={referenceImageId}
            onChange={(id) => {
              setReferenceImageId(id);
              if (!id) setConfidence('');
            }}
            disabled={disabled}
          />
        </ParameterSourceControl>
      </div>
      <div className="field">
        <label htmlFor="wfi-panel-confidence">Confidence (0–1)</label>
        <input
          id="wfi-panel-confidence"
          type="number"
          step="0.01"
          min="0"
          max="1"
          value={confidence}
          onChange={(e) => setConfidence(e.target.value)}
          disabled={disabled}
        />
        {attempted && confidenceError && <div className="field-error" role="alert">{confidenceError}</div>}
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
