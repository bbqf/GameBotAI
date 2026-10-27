import React, { useState } from 'react';

export type KeyInputPanelValue = {
  key: string;
};

export type KeyInputPanelProps = {
  initialValue?: KeyInputPanelValue;
  onConfirm: (value: KeyInputPanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
};

export const KeyInputPanel: React.FC<KeyInputPanelProps> = ({ initialValue, onConfirm, onCancel, disabled }) => {
  const [key, setKey] = useState(initialValue?.key ?? '');
  const [attempted, setAttempted] = useState(false);

  const keyError = !key.trim() ? 'Key identifier is required.' : null;
  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (keyError) return;
    onConfirm({ key: key.trim() });
  };

  return (
    <div className="action-panel action-panel--key-input">
      <div className="field">
        <label htmlFor="key-input-panel-key">Key identifier *</label>
        <input
          id="key-input-panel-key"
          type="text"
          value={key}
          onChange={(e) => setKey(e.target.value)}
          placeholder="e.g. Enter, Escape, 6, a, 85"
          disabled={disabled}
          aria-invalid={attempted && Boolean(keyError)}
          aria-describedby={attempted && keyError ? 'key-input-panel-key-error key-input-panel-key-help' : 'key-input-panel-key-help'}
        />
        <div id="key-input-panel-key-help" className="form-hint">
          Use a key name (for example Enter), a digit 0 to 9, a number pad name NUMPAD_0 to NUMPAD_9, or a raw Android key code from 0 to 300.
          A name has priority over a raw key code.
        </div>
        {attempted && keyError && (
          <div id="key-input-panel-key-error" className="field-error" role="alert">{keyError}</div>
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
