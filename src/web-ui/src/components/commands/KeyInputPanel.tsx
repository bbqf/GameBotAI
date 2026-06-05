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
          placeholder="e.g. Enter, Escape, F5, a"
          disabled={disabled}
          aria-invalid={attempted && Boolean(keyError)}
          aria-describedby={attempted && keyError ? 'key-input-panel-key-error' : undefined}
        />
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
