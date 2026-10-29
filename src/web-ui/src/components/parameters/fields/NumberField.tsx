import React, { useEffect, useState } from 'react';
import { checkParameterValue } from '../parameterValueRules';
import type { ParameterFieldProps } from './fieldProps';

const rangeHint = (min?: number | null, max?: number | null): string | null => {
  if (min != null && max != null) return `Enter a number from ${min} to ${max}.`;
  if (min != null) return `Enter a number of ${min} or more.`;
  if (max != null) return `Enter a number of ${max} or less.`;
  return null;
};

/**
 * The number field of a number parameter (feature 168). It shows the unit and the range hint. A new
 * value that is outside the range shows a message with a hint and is not accepted. A stored value
 * that is outside the range (the author changed the range) shows a message, and the form does not
 * change it (FR-013).
 */
export const NumberField: React.FC<ParameterFieldProps> = ({ declaration, value, onChange, disabled, ariaLabel }) => {
  const [draft, setDraft] = useState(value ?? '');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    setDraft(value ?? '');
    setMessage(value ? checkParameterValue(declaration, value) : null);
  }, [value, declaration]);

  const hint = rangeHint(declaration.min, declaration.max);
  const messageId = `parameter-number-message-${declaration.name}`;
  const hintId = `parameter-number-hint-${declaration.name}`;

  const handleChange = (next: string) => {
    setDraft(next);
    const problem = next === '' ? null : checkParameterValue(declaration, next);
    setMessage(problem);
    if (problem === null) onChange(next);
  };

  return (
    <span className="parameter-value-field number-field">
      <input
        type="text"
        inputMode="numeric"
        className="parameter-binding-value"
        value={draft}
        disabled={disabled}
        aria-label={ariaLabel}
        aria-invalid={message ? true : undefined}
        aria-describedby={[hint ? hintId : null, message ? messageId : null].filter(Boolean).join(' ') || undefined}
        onChange={(e) => handleChange(e.target.value)}
      />
      {declaration.unit && <span className="parameter-value-unit">{declaration.unit}</span>}
      {hint && (
        <span id={hintId} className="parameter-value-hint">
          {hint}
        </span>
      )}
      {message && (
        <span id={messageId} className="parameter-value-message" role="alert">
          {message}
        </span>
      )}
    </span>
  );
};
