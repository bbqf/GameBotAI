import React, { useState } from 'react';
import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown';
import { ParameterSourceControl } from '../parameters/ParameterSourceControl';
import { useFieldTemplates } from '../parameters/useFieldTemplates';
import type { ParameterDeclaration } from '../parameters/types';

export type TapPanelValue = {
  referenceImageId: string;
  confidence?: string;
  offsetX?: string;
  offsetY?: string;
  /** Hold duration in ms. Empty gives a single tap. */
  holdMs?: string;
  /** Placeholders for step fields (feature 169). Includes keys that this panel has no control for. */
  fieldTemplates?: Record<string, string>;
};

export type TapPanelProps = {
  initialValue?: TapPanelValue;
  onConfirm: (value: TapPanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
  /** Parameters that the command declares (feature 169). */
  parameters?: ParameterDeclaration[];
  /** Placeholders of the step that is in edit. */
  fieldTemplates?: Record<string, string>;
  /** Service messages by field path, for this step only. */
  fieldErrors?: Record<string, string>;
};

const IMAGE_PATH = 'primitiveTap.detectionTarget.referenceImageId';
const CONFIDENCE_PATH = 'primitiveTap.detectionTarget.confidence';
const OFFSET_X_PATH = 'primitiveTap.detectionTarget.offsetX';
const OFFSET_Y_PATH = 'primitiveTap.detectionTarget.offsetY';

// The service refuses a tap step with an empty inline image, also with an image placeholder
// (see ParametrizedReferenceImageIntegrationTests). So a person selects a stored image first.
const SELECT_IMAGE_FIRST = 'Select a stored image first. Then select the parameter.';

const validateConfidence = (confidence: string): string | null => {
  if (!confidence.trim()) return null;
  const n = parseFloat(confidence);
  if (isNaN(n) || n < 0 || n > 1) return 'Confidence must be a number between 0 and 1.';
  return null;
};

const validateHoldMs = (holdMs: string): string | null => {
  if (!holdMs.trim()) return null;
  const n = Number(holdMs);
  if (!Number.isInteger(n) || n < 0 || n > 5000) return 'Hold duration must be an integer from 0 to 5000.';
  return null;
};

export const TapPanel: React.FC<TapPanelProps> = ({
  initialValue,
  onConfirm,
  onCancel,
  disabled,
  parameters,
  fieldTemplates,
  fieldErrors,
}) => {
  const templates = useFieldTemplates(fieldTemplates);
  const [referenceImageId, setReferenceImageId] = useState(initialValue?.referenceImageId ?? '');
  const [confidence, setConfidence] = useState(initialValue?.confidence ?? '');
  const [offsetX, setOffsetX] = useState(initialValue?.offsetX ?? '0');
  const [offsetY, setOffsetY] = useState(initialValue?.offsetY ?? '0');
  const [holdMs, setHoldMs] = useState(initialValue?.holdMs ?? '');
  const [stale, setStale] = useState(false);
  const [attempted, setAttempted] = useState(false);

  const imageTemplate = templates.get(IMAGE_PATH);
  // With a placeholder, the kept image only has to be present: the placeholder replaces it at run time.
  const imageError = imageTemplate
    ? (referenceImageId.trim() ? null : 'Reference image is required.')
    : (!referenceImageId.trim() || stale ? 'Reference image is required.' : null);
  const confidenceError = templates.get(CONFIDENCE_PATH) ? null : validateConfidence(confidence);
  const holdMsError = validateHoldMs(holdMs);
  const hasErrors = Boolean(imageError || confidenceError || holdMsError);

  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (hasErrors) return;
    onConfirm({
      referenceImageId: referenceImageId.trim(),
      confidence: confidence.trim() || undefined,
      offsetX: offsetX.trim() || undefined,
      offsetY: offsetY.trim() || undefined,
      holdMs: holdMs.trim() || undefined,
      fieldTemplates: templates.result(),
    });
  };

  const control = (path: string, label: string, child: React.ReactNode, blockedMessage?: string) => (
    <ParameterSourceControl
      path={path}
      label={label}
      parameters={parameters ?? []}
      template={templates.get(path)}
      onChange={(t) => templates.set(path, t)}
      disabled={disabled}
      error={fieldErrors?.[path]}
      blockedMessage={blockedMessage}
    >
      {child}
    </ParameterSourceControl>
  );

  return (
    <div className="action-panel action-panel--tap">
      <div className="field">
        {imageTemplate && <span className="field-label">Reference image *</span>}
        {control(
          IMAGE_PATH,
          'Reference image',
          <ImageSelectorDropdown
            id="tap-panel-image"
            label="Reference image *"
            value={referenceImageId}
            onChange={(id) => { setReferenceImageId(id); setStale(false); }}
            onStaleChange={setStale}
            required
            error={attempted && imageError ? imageError : undefined}
            disabled={disabled}
          />,
          referenceImageId.trim() && !stale ? undefined : SELECT_IMAGE_FIRST,
        )}
        {imageTemplate && attempted && imageError && (
          <div className="field-error" role="alert">{imageError}</div>
        )}
      </div>
      <div className="field">
        <label htmlFor="tap-panel-confidence">Confidence (0–1)</label>
        {control(
          CONFIDENCE_PATH,
          'Confidence',
          <input
            id="tap-panel-confidence"
            type="number"
            step="0.01"
            min="0"
            max="1"
            value={confidence}
            onChange={(e) => setConfidence(e.target.value)}
            disabled={disabled}
          />,
        )}
        {attempted && confidenceError && <div className="field-error" role="alert">{confidenceError}</div>}
      </div>
      <div className="field">
        <label htmlFor="tap-panel-offset-x">Offset X</label>
        {control(
          OFFSET_X_PATH,
          'Offset X',
          <input
            id="tap-panel-offset-x"
            type="number"
            value={offsetX}
            onChange={(e) => setOffsetX(e.target.value)}
            disabled={disabled}
          />,
        )}
      </div>
      <div className="field">
        <label htmlFor="tap-panel-offset-y">Offset Y</label>
        {control(
          OFFSET_Y_PATH,
          'Offset Y',
          <input
            id="tap-panel-offset-y"
            type="number"
            value={offsetY}
            onChange={(e) => setOffsetY(e.target.value)}
            disabled={disabled}
          />,
        )}
      </div>
      <div className="field">
        <label htmlFor="tap-panel-hold-ms">Hold duration (ms)</label>
        <input
          id="tap-panel-hold-ms"
          type="number"
          step="1"
          min="0"
          max="5000"
          value={holdMs}
          onChange={(e) => setHoldMs(e.target.value)}
          disabled={disabled}
          aria-describedby="tap-panel-hold-ms-hint"
        />
        <div id="tap-panel-hold-ms-hint" className="form-hint">
          Empty or 0: a single tap. 1 to 5000: press and hold at the image for this duration.
        </div>
        {attempted && holdMsError && <div className="field-error" role="alert">{holdMsError}</div>}
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
