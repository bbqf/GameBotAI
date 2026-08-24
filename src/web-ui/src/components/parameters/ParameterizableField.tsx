import React, { useMemo, useRef, useState } from 'react';
import type { ParameterScopeEntry } from './types';
import { isBuiltIn, originLayerLabel, toReference } from './types';

export type ParameterizableFieldProps = {
  id?: string;
  label?: string;
  value: string;
  onChange: (value: string) => void;
  /** Names currently in scope, from the backend's `/parameter-scope`. */
  scope: ParameterScopeEntry[];
  /**
   * Numeric fields accept only a whole-field placeholder, so inserting replaces the value outright
   * rather than splicing into surrounding text.
   */
  numeric?: boolean;
  placeholder?: string;
  disabled?: boolean;
  /** Inline validation message anchored at this field (FR-029). */
  error?: string;
  /** Non-blocking notice anchored at this field, e.g. a skipped static check. */
  warning?: string;
};

/**
 * A text input with an insert-parameter affordance (feature 078, FR-026).
 *
 * The operator never types `{{` `}}` by hand: the picker lists only names that are actually in scope
 * — the entity's own declarations plus the reserved queue built-ins — each with its description, and
 * inserting one produces a valid reference. That is what makes parametrizing faster than duplicating
 * the entity, which is the whole point of the feature.
 */
export const ParameterizableField: React.FC<ParameterizableFieldProps> = ({
  id,
  label,
  value,
  onChange,
  scope,
  numeric,
  placeholder,
  disabled,
  error,
  warning,
}) => {
  const [pickerOpen, setPickerOpen] = useState(false);
  const [query, setQuery] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return scope;
    return scope.filter(
      (entry) =>
        entry.name.toLowerCase().includes(q) ||
        (entry.description ?? '').toLowerCase().includes(q),
    );
  }, [scope, query]);

  const insert = (name: string) => {
    const reference = toReference(name);
    if (numeric) {
      // A numeric field must be exactly one reference so the resolved value parses.
      onChange(reference);
    } else {
      const input = inputRef.current;
      const start = input?.selectionStart ?? value.length;
      const end = input?.selectionEnd ?? value.length;
      onChange(value.slice(0, start) + reference + value.slice(end));
    }
    setPickerOpen(false);
    setQuery('');
  };

  const fieldId = id ?? (label ? `param-field-${label.replace(/\s+/g, '-').toLowerCase()}` : undefined);
  const errorId = error ? `${fieldId ?? 'param-field'}-error` : undefined;

  return (
    <div className={`parameterizable-field${error ? ' parameterizable-field-error' : ''}`}>
      {label && (
        <label htmlFor={fieldId} className="parameterizable-field-label">
          {label}
        </label>
      )}
      <div className="parameterizable-field-row">
        <input
          ref={inputRef}
          id={fieldId}
          className="parameterizable-field-input"
          type="text"
          value={value}
          placeholder={placeholder}
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-describedby={errorId}
          onChange={(e) => onChange(e.target.value)}
        />
        <button
          type="button"
          className="parameterizable-field-insert"
          disabled={disabled || scope.length === 0}
          aria-label={label ? `Insert parameter into ${label}` : 'Insert parameter'}
          aria-expanded={pickerOpen}
          title="Insert a parameter"
          onClick={() => setPickerOpen((open) => !open)}
        >
          {'{ }'}
        </button>
      </div>

      {pickerOpen && (
        <div className="parameterizable-field-picker" role="listbox" aria-label="Parameters in scope">
          <input
            className="parameterizable-field-search"
            aria-label="Search parameters"
            placeholder="Search parameters…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          {filtered.length === 0 ? (
            <p className="parameterizable-field-empty">
              Nothing in scope. Declare a parameter first, or use a queue built-in.
            </p>
          ) : (
            <ul className="parameterizable-field-options">
              {filtered.map((entry) => (
                <li key={entry.name}>
                  <button
                    type="button"
                    role="option"
                    aria-selected={false}
                    className="parameterizable-field-option"
                    onClick={() => insert(entry.name)}
                  >
                    <span className="parameterizable-field-option-name">{entry.name}</span>
                    {isBuiltIn(entry.name) && (
                      <span className="parameterizable-field-badge">built-in</span>
                    )}
                    {entry.description && (
                      <span className="parameterizable-field-option-desc">{entry.description}</span>
                    )}
                    <span className="parameterizable-field-option-origin">
                      {originLayerLabel(entry.originLayer)}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {numeric && (
        <p className="parameterizable-field-hint">
          A numeric field must be either a plain number or exactly one parameter.
        </p>
      )}
      {error && (
        <p id={errorId} className="parameterizable-field-error-text" role="alert">
          {error}
        </p>
      )}
      {!error && warning && <p className="parameterizable-field-warning-text">{warning}</p>}
    </div>
  );
};
