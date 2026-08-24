import React, { useState } from 'react';
import type { ParameterBinding, ParameterDeclaration, ParameterScopeEntry } from './types';
import { originLayerLabel } from './types';
import { validateParameterName } from './ParameterDeclarationList';

export type ParameterBindingFormProps = {
  /** What the callee declares. Every row starts on "inherit", so the common case is zero clicks. */
  declarations: ParameterDeclaration[];
  bindings: ParameterBinding[];
  onChange: (bindings: ParameterBinding[]) => void;
  /**
   * Effective value and origin per name, when the backend could compute it (queue-template entries).
   * Omitted on a sequence's command step, where no queue context exists yet.
   */
  effective?: ParameterScopeEntry[];
  /**
   * Allows name/value pairs beyond the declared list (feature 078, FR-012a). Valid only on a
   * queue-template entry — the outermost call site — where an ad-hoc name can reach a command at any
   * depth. A sequence's command step binds only what its command declares, so a typo there stays a
   * hard error instead of becoming a silently unused value.
   */
  allowAdHoc?: boolean;
  disabled?: boolean;
  /** Non-blocking advisories keyed by parameter name, e.g. "used by nothing in this entry". */
  warnings?: Record<string, string>;
};

const INHERIT = null;

/**
 * Renders a callee's parameters as a binding form (feature 078, FR-027/FR-028).
 *
 * Every row defaults to **Inherit**, which is what makes the motivating case free: a queue already
 * supplies its emulator serial, so the operator changes nothing. Switching a row to a value overrides
 * for this call site only.
 */
export const ParameterBindingForm: React.FC<ParameterBindingFormProps> = ({
  declarations,
  bindings,
  onChange,
  effective,
  allowAdHoc,
  disabled,
  warnings,
}) => {
  const [adHocName, setAdHocName] = useState('');
  const [adHocError, setAdHocError] = useState<string | undefined>();

  const bindingFor = (name: string) => bindings.find((b) => b.name === name);

  const setValue = (name: string, value: string | null) => {
    const existing = bindingFor(name);
    if (!existing) {
      onChange([...bindings, { name, value }]);
      return;
    }
    onChange(bindings.map((b) => (b.name === name ? { ...b, value } : b)));
  };

  const removeBinding = (name: string) => onChange(bindings.filter((b) => b.name !== name));

  const effectiveFor = (name: string) => effective?.find((e) => e.name === name);

  const declaredNames = new Set(declarations.map((d) => d.name));
  const adHocBindings = bindings.filter((b) => !declaredNames.has(b.name));

  const addAdHoc = () => {
    const name = adHocName.trim();
    const error = validateParameterName(name, bindings.map((b) => b.name));
    if (error) {
      setAdHocError(error);
      return;
    }
    onChange([...bindings, { name, value: '' }]);
    setAdHocName('');
    setAdHocError(undefined);
  };

  const renderRow = (name: string, declaration?: ParameterDeclaration, adHoc = false) => {
    const binding = bindingFor(name);
    const inheriting = !binding || binding.value === INHERIT || binding.value === undefined;
    const resolved = effectiveFor(name);
    const warning = warnings?.[name];

    return (
      <li key={name} className={`parameter-binding-row${adHoc ? ' parameter-binding-adhoc' : ''}`}>
        <div className="parameter-binding-head">
          <span className="parameter-binding-name">{name}</span>
          {adHoc && <span className="parameter-binding-badge">ad-hoc</span>}
          {declaration?.required && <span className="parameter-binding-required">required</span>}
          {declaration?.type === 'number' && <span className="parameter-binding-type">number</span>}
        </div>

        {declaration?.description && (
          <p className="parameter-binding-description">{declaration.description}</p>
        )}

        <div className="parameter-binding-controls">
          {!adHoc && (
            <label className="parameter-binding-inherit">
              <input
                type="checkbox"
                checked={inheriting}
                disabled={disabled}
                aria-label={`Inherit ${name}`}
                onChange={(e) => setValue(name, e.target.checked ? INHERIT : '')}
              />
              <span>Inherit</span>
            </label>
          )}
          <input
            type="text"
            className="parameter-binding-value"
            value={binding?.value ?? ''}
            disabled={disabled || (!adHoc && inheriting)}
            placeholder={inheriting ? 'inherited' : ''}
            aria-label={`Value for ${name}`}
            onChange={(e) => setValue(name, e.target.value)}
          />
          {adHoc && (
            <button
              type="button"
              disabled={disabled}
              aria-label={`Remove ${name}`}
              onClick={() => removeBinding(name)}
            >
              Remove
            </button>
          )}
        </div>

        {inheriting && resolved && (
          <p className="parameter-binding-effective">
            {resolved.value == null ? (
              <>
                Nothing in scope supplies this yet
                {declaration?.required ? ' — the queue will refuse to start.' : '.'}
              </>
            ) : (
              <>
                Resolves to <code>{resolved.value}</code> ({originLayerLabel(resolved.originLayer)})
              </>
            )}
          </p>
        )}
        {inheriting && !resolved && declaration?.default != null && (
          <p className="parameter-binding-effective">
            Falls back to the declared default <code>{declaration.default}</code>.
          </p>
        )}
        {warning && <p className="parameter-binding-warning">{warning}</p>}
      </li>
    );
  };

  if (declarations.length === 0 && adHocBindings.length === 0 && !allowAdHoc) {
    return null;
  }

  return (
    <section className="parameter-bindings" aria-label="Parameter values">
      <h4>Parameters</h4>
      {declarations.length === 0 && adHocBindings.length === 0 ? (
        <p className="parameter-bindings-empty">
          Nothing here declares a parameter. You can still add a value below — it reaches any command
          underneath that declares that name.
        </p>
      ) : (
        <ul className="parameter-bindings-list">
          {declarations.map((declaration) => renderRow(declaration.name, declaration))}
          {adHocBindings.map((binding) => renderRow(binding.name, undefined, true))}
        </ul>
      )}

      {allowAdHoc && (
        <div className="parameter-bindings-adhoc-add">
          <label>
            <span>Add value</span>
            <input
              type="text"
              value={adHocName}
              disabled={disabled}
              placeholder="e.g. adbSerial"
              aria-label="New parameter value name"
              onChange={(e) => {
                setAdHocName(e.target.value);
                setAdHocError(undefined);
              }}
            />
          </label>
          <button type="button" disabled={disabled || !adHocName.trim()} onClick={addAdHoc}>
            Add
          </button>
          {adHocError && (
            <p className="parameter-bindings-error" role="alert">
              {adHocError}
            </p>
          )}
        </div>
      )}
    </section>
  );
};
