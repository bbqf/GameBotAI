import React, { useState } from 'react';
import type { ParameterDeclaration, ParameterValueType } from './types';
import { BUILT_IN_PREFIX } from './types';

export type ParameterDeclarationListProps = {
  parameters: ParameterDeclaration[];
  onChange: (parameters: ParameterDeclaration[]) => void;
  disabled?: boolean;
  /** What owns these declarations, used in the empty-state copy. */
  ownerLabel?: string;
};

const RESERVED_ITERATION = 'iteration';
const IDENTIFIER = /^[A-Za-z_]\w*$/;

/**
 * Mirrors the backend's ParameterNameRules so the operator gets the message while typing rather than
 * on save. The backend remains authoritative — this is a convenience, not the enforcement point.
 */
export const validateParameterName = (
  name: string,
  others: readonly string[],
): string | undefined => {
  if (!name.trim()) return 'Name is required.';
  if (name === RESERVED_ITERATION) return "'iteration' is reserved for the loop iteration value.";
  if (name === 'queue' || name.startsWith(BUILT_IN_PREFIX)) {
    return "Names may not use the reserved 'queue' namespace.";
  }
  if (!IDENTIFIER.test(name)) {
    return 'Use letters, digits and underscore; do not start with a digit.';
  }
  if (others.some((other) => other.toLowerCase() === name.toLowerCase())) {
    return 'Another parameter already uses this name.';
  }
  return undefined;
};

const emptyDeclaration = (): ParameterDeclaration => ({
  name: '',
  type: 'text',
  default: null,
  required: false,
  description: '',
});

/**
 * The Parameters section of the command and sequence editors (feature 078, FR-025).
 *
 * Declaring a parameter is what makes it appear in the insert-parameter picker and in the binding
 * form on every call site, so this is the entry point to the whole mechanism.
 */
export const ParameterDeclarationList: React.FC<ParameterDeclarationListProps> = ({
  parameters,
  onChange,
  disabled,
  ownerLabel = 'this item',
}) => {
  const [touched, setTouched] = useState<Record<number, boolean>>({});

  const update = (index: number, patch: Partial<ParameterDeclaration>) => {
    onChange(parameters.map((p, i) => (i === index ? { ...p, ...patch } : p)));
  };

  const remove = (index: number) => onChange(parameters.filter((_, i) => i !== index));

  const move = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= parameters.length) return;
    const next = [...parameters];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };

  return (
    <section className="parameter-declarations" aria-label="Parameters">
      <header className="parameter-declarations-header">
        <h3>Parameters</h3>
        <button
          type="button"
          className="parameter-declarations-add"
          disabled={disabled}
          onClick={() => onChange([...parameters, emptyDeclaration()])}
        >
          Add parameter
        </button>
      </header>

      {parameters.length === 0 ? (
        <p className="parameter-declarations-empty">
          {ownerLabel} has no parameters. Add one to let a caller supply a value instead of hard-coding
          it — or reference a <code>queue.…</code> built-in directly, which needs no declaration.
        </p>
      ) : (
        <ul className="parameter-declarations-list">
          {parameters.map((parameter, index) => {
            const others = parameters.filter((_, i) => i !== index).map((p) => p.name);
            const nameError = touched[index] ? validateParameterName(parameter.name, others) : undefined;
            const defaultError =
              parameter.type === 'number' &&
              parameter.default != null &&
              parameter.default !== '' &&
              !/^-?\d+$/.test(parameter.default)
                ? 'A numeric default must be a whole number.'
                : undefined;

            return (
              <li key={index} className="parameter-declaration-row">
                <div className="parameter-declaration-fields">
                  <label>
                    <span>Name</span>
                    <input
                      type="text"
                      value={parameter.name}
                      disabled={disabled}
                      aria-invalid={nameError ? true : undefined}
                      aria-label={`Parameter ${index + 1} name`}
                      onBlur={() => setTouched((t) => ({ ...t, [index]: true }))}
                      onChange={(e) => update(index, { name: e.target.value })}
                    />
                  </label>

                  <label>
                    <span>Type</span>
                    <select
                      value={parameter.type}
                      disabled={disabled}
                      aria-label={`Parameter ${index + 1} type`}
                      onChange={(e) => update(index, { type: e.target.value as ParameterValueType })}
                    >
                      <option value="text">Text</option>
                      <option value="number">Number</option>
                    </select>
                  </label>

                  <label>
                    <span>Default</span>
                    <input
                      type="text"
                      value={parameter.default ?? ''}
                      disabled={disabled}
                      placeholder="(none)"
                      aria-label={`Parameter ${index + 1} default`}
                      onChange={(e) => update(index, { default: e.target.value === '' ? null : e.target.value })}
                    />
                  </label>

                  <label className="parameter-declaration-required">
                    <input
                      type="checkbox"
                      checked={parameter.required}
                      disabled={disabled}
                      aria-label={`Parameter ${index + 1} required`}
                      onChange={(e) => update(index, { required: e.target.checked })}
                    />
                    <span>Required</span>
                  </label>
                </div>

                <label className="parameter-declaration-description">
                  <span>Description</span>
                  <input
                    type="text"
                    value={parameter.description ?? ''}
                    disabled={disabled}
                    placeholder="Shown in the insert-parameter picker"
                    aria-label={`Parameter ${index + 1} description`}
                    onChange={(e) => update(index, { description: e.target.value })}
                  />
                </label>

                <div className="parameter-declaration-actions">
                  <button
                    type="button"
                    disabled={disabled || index === 0}
                    aria-label={`Move ${parameter.name || `parameter ${index + 1}`} up`}
                    onClick={() => move(index, -1)}
                  >
                    ↑
                  </button>
                  <button
                    type="button"
                    disabled={disabled || index === parameters.length - 1}
                    aria-label={`Move ${parameter.name || `parameter ${index + 1}`} down`}
                    onClick={() => move(index, 1)}
                  >
                    ↓
                  </button>
                  <button
                    type="button"
                    disabled={disabled}
                    aria-label={`Remove ${parameter.name || `parameter ${index + 1}`}`}
                    onClick={() => remove(index)}
                  >
                    Remove
                  </button>
                </div>

                {nameError && (
                  <p className="parameter-declaration-error" role="alert">
                    {nameError}
                  </p>
                )}
                {defaultError && (
                  <p className="parameter-declaration-error" role="alert">
                    {defaultError}
                  </p>
                )}
                {parameter.required && (parameter.default ?? '') === '' && (
                  <p className="parameter-declaration-hint">
                    Required with no default: every queue that runs this must supply a value, or its
                    start is refused.
                  </p>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
};
