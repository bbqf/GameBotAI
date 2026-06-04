import React, { useState } from 'react';
import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown';

export type TapPanelValue = {
  referenceImageId: string;
  confidence?: string;
  offsetX?: string;
  offsetY?: string;
};

export type TapPanelProps = {
  initialValue?: TapPanelValue;
  onConfirm: (value: TapPanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
};

const validateConfidence = (confidence: string): string | null => {
  if (!confidence.trim()) return null;
  const n = parseFloat(confidence);
  if (isNaN(n) || n < 0 || n > 1) return 'Confidence must be a number between 0 and 1.';
  return null;
};

export const TapPanel: React.FC<TapPanelProps> = ({ initialValue, onConfirm, onCancel, disabled }) => {
  const [referenceImageId, setReferenceImageId] = useState(initialValue?.referenceImageId ?? '');
  const [confidence, setConfidence] = useState(initialValue?.confidence ?? '');
  const [offsetX, setOffsetX] = useState(initialValue?.offsetX ?? '0');
  const [offsetY, setOffsetY] = useState(initialValue?.offsetY ?? '0');
  const [stale, setStale] = useState(false);
  const [attempted, setAttempted] = useState(false);

  const imageError = !referenceImageId.trim() || stale ? 'Reference image is required.' : null;
  const confidenceError = validateConfidence(confidence);
  const hasErrors = Boolean(imageError || confidenceError);

  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (hasErrors) return;
    onConfirm({
      referenceImageId: referenceImageId.trim(),
      confidence: confidence.trim() || undefined,
      offsetX: offsetX.trim() || undefined,
      offsetY: offsetY.trim() || undefined,
    });
  };

  return (
    <div className="action-panel action-panel--tap">
      <div className="field">
        <ImageSelectorDropdown
          id="tap-panel-image"
          label="Reference image *"
          value={referenceImageId}
          onChange={(id) => { setReferenceImageId(id); setStale(false); }}
          onStaleChange={setStale}
          required
          error={attempted && imageError ? imageError : undefined}
          disabled={disabled}
        />
      </div>
      <div className="field">
        <label htmlFor="tap-panel-confidence">Confidence (0–1)</label>
        <input
          id="tap-panel-confidence"
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
      <div className="field">
        <label htmlFor="tap-panel-offset-x">Offset X</label>
        <input
          id="tap-panel-offset-x"
          type="number"
          value={offsetX}
          onChange={(e) => setOffsetX(e.target.value)}
          disabled={disabled}
        />
      </div>
      <div className="field">
        <label htmlFor="tap-panel-offset-y">Offset Y</label>
        <input
          id="tap-panel-offset-y"
          type="number"
          value={offsetY}
          onChange={(e) => setOffsetY(e.target.value)}
          disabled={disabled}
        />
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
