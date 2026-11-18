import React, { useState } from 'react';
import { ParameterSourceControl } from '../parameters/ParameterSourceControl';
import { useFieldTemplates } from '../parameters/useFieldTemplates';
import type { ParameterDeclaration } from '../parameters/types';

export type TextInputPanelValue = {
  text: string;
  /** Placeholders for step fields (feature 192). Includes keys that this panel has no control for. */
  fieldTemplates?: Record<string, string>;
};

export type TextInputPanelProps = {
  initialValue?: TextInputPanelValue;
  onConfirm: (value: TextInputPanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
  /** Parameters that the command declares. */
  parameters?: ParameterDeclaration[];
  /** Placeholders of the step that is in edit. */
  fieldTemplates?: Record<string, string>;
  /** Service messages by field path, for this step only. */
  fieldErrors?: Record<string, string>;
};

const TEXT_PATH = 'textInput.text';
const MAX_LENGTH = 500;
const PLACEHOLDER = /\{\{[^}]*\}\}/;
const PRINTABLE_ASCII = /^[\x20-\x7E]*$/;

export const TEXT_REQUIRED = 'Text is required.';
export const TEXT_TOO_LONG = `Text must have ${MAX_LENGTH} characters or fewer.`;
export const TEXT_UNSUPPORTED = 'Text can have only the printable ASCII characters from space to ~.';

/** Checks a literal text. A text with a placeholder is checked by the service at run time. */
const validateText = (text: string): string | null => {
  if (PLACEHOLDER.test(text)) return null;
  if (text.length === 0) return TEXT_REQUIRED;
  if (text.length > MAX_LENGTH) return TEXT_TOO_LONG;
  if (!PRINTABLE_ASCII.test(text)) return TEXT_UNSUPPORTED;
  return null;
};

/**
 * Feature 192: the editor panel of a TextInput step. The device types the full text into the input
 * field that has the focus. The text can come from a parameter.
 */
export const TextInputPanel: React.FC<TextInputPanelProps> = ({
  initialValue,
  onConfirm,
  onCancel,
  disabled,
  parameters,
  fieldTemplates,
  fieldErrors,
}) => {
  const templates = useFieldTemplates(fieldTemplates);
  const [text, setText] = useState(initialValue?.text ?? '');
  const [attempted, setAttempted] = useState(false);

  // A field with a parameter is valid: the service checks the value that the parameter gives.
  const textError = templates.get(TEXT_PATH) ? null : validateText(text);
  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (textError) return;
    onConfirm({ text, fieldTemplates: templates.result() });
  };

  return (
    <div className="action-panel action-panel--text-input">
      <div className="field">
        <label htmlFor="text-input-panel-text">Text *</label>
        <ParameterSourceControl
          path={TEXT_PATH}
          label="Text"
          parameters={parameters ?? []}
          template={templates.get(TEXT_PATH)}
          onChange={(t) => templates.set(TEXT_PATH, t)}
          disabled={disabled}
          error={fieldErrors?.[TEXT_PATH]}
        >
          <input
            id="text-input-panel-text"
            type="text"
            value={text}
            maxLength={MAX_LENGTH}
            onChange={(e) => setText(e.target.value)}
            placeholder="e.g. 250"
            disabled={disabled}
            aria-invalid={attempted && Boolean(textError)}
            aria-describedby={attempted && textError ? 'text-input-panel-text-error text-input-panel-text-help' : 'text-input-panel-text-help'}
          />
        </ParameterSourceControl>
        <div id="text-input-panel-text-help" className="form-hint">
          The device types the full text into the input field that has the focus. Use only the printable ASCII characters
          from space to ~, up to {MAX_LENGTH} characters. Tap the input field in an earlier step.
        </div>
        {attempted && textError && (
          <div id="text-input-panel-text-error" className="field-error" role="alert">{textError}</div>
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
