import React from 'react';
import type { ParameterOption, ParameterValueType } from './types';

export const MAX_OPTIONS = 50;

export type OptionsEditorProps = {
  options: ParameterOption[];
  /** The value type follows the parameter type: a number parameter needs whole-number values. */
  valueType: ParameterValueType;
  onChange: (options: ParameterOption[]) => void;
  disabled?: boolean;
};

/** Finds the problem of one option row, with a hint. Returns undefined when the row is fine. */
export const optionProblem = (
  option: ParameterOption,
  index: number,
  all: readonly ParameterOption[],
  valueType: ParameterValueType,
): string | undefined => {
  if (!option.label.trim()) return 'The option needs a label. Write the text that the person sees.';
  if (option.value === '') return 'The option needs a value. Write the technical value.';
  if (all.some((other, i) => i !== index && other.label.trim().toLowerCase() === option.label.trim().toLowerCase())) {
    return `The label '${option.label}' is used two times. Give each option a different label.`;
  }
  if (all.some((other, i) => i !== index && other.value === option.value)) {
    return `The value '${option.value}' is used two times. Give each option a different value.`;
  }
  if (valueType === 'number' && !/^-?\d+$/.test(option.value)) {
    return 'The value must be a whole number. Write a whole number.';
  }
  if (/^\{\{[^{}]+\}\}$/.test(option.value)) {
    return 'The value is only a placeholder. Add text around it, or use a parameter of the sequence at the step.';
  }
  return undefined;
};

/** The author edits the options of a choice: label, technical value, order (feature 168). */
export const OptionsEditor: React.FC<OptionsEditorProps> = ({ options, valueType, onChange, disabled }) => {
  const update = (index: number, patch: Partial<ParameterOption>) =>
    onChange(options.map((option, i) => (i === index ? { ...option, ...patch } : option)));

  const move = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= options.length) return;
    const next = [...options];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };

  return (
    <div className="options-editor">
      <ul className="options-editor-list">
        {options.map((option, index) => {
          const problem = optionProblem(option, index, options, valueType);
          return (
            <li key={index} className="options-editor-row">
              <input
                type="text"
                value={option.label}
                disabled={disabled}
                placeholder="Label (the person sees this)"
                aria-label={`Option ${index + 1} label`}
                aria-invalid={problem ? true : undefined}
                onChange={(e) => update(index, { label: e.target.value })}
              />
              <input
                type="text"
                value={option.value}
                disabled={disabled}
                placeholder="Value (the sequence gets this)"
                aria-label={`Option ${index + 1} value`}
                aria-invalid={problem ? true : undefined}
                onChange={(e) => update(index, { value: e.target.value })}
              />
              <button type="button" disabled={disabled || index === 0} aria-label={`Move option ${index + 1} up`} onClick={() => move(index, -1)}>
                ↑
              </button>
              <button
                type="button"
                disabled={disabled || index === options.length - 1}
                aria-label={`Move option ${index + 1} down`}
                onClick={() => move(index, 1)}
              >
                ↓
              </button>
              <button
                type="button"
                disabled={disabled || options.length <= 1}
                aria-label={`Remove option ${index + 1}`}
                onClick={() => onChange(options.filter((_, i) => i !== index))}
              >
                Remove
              </button>
              {problem && (
                <p className="parameter-declaration-error" role="alert">
                  {problem}
                </p>
              )}
            </li>
          );
        })}
      </ul>
      <button
        type="button"
        disabled={disabled || options.length >= MAX_OPTIONS}
        onClick={() => onChange([...options, { label: '', value: '' }])}
      >
        Add option
      </button>
      {options.length >= MAX_OPTIONS && (
        <p className="parameter-declaration-hint">A choice has up to {MAX_OPTIONS} options. Remove one to add another.</p>
      )}
    </div>
  );
};
