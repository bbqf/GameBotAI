import React, { useRef } from 'react';
import type { ParameterDeclaration, ParameterInputKind, ParameterValueType } from './types';
import { effectiveKind } from './types';
import { validateParameterName } from './parameterNameRules';
import { suggestParameterName } from './suggestParameterName';
import { OptionsEditor } from './OptionsEditor';
import { NumberSettings } from './NumberSettings';
import { AllowedImagesEditor } from './AllowedImagesEditor';

export type ParameterDeclarationCardProps = {
  parameter: ParameterDeclaration;
  index: number;
  /** The names of the other parameters of the same item. */
  otherNames: readonly string[];
  count: number;
  touched: boolean;
  onTouched: () => void;
  onChange: (patch: Partial<ParameterDeclaration>) => void;
  onMove: (delta: number) => void;
  onRemove: () => void;
  disabled?: boolean;
};

const KIND_LABELS: Record<ParameterInputKind, string> = {
  text: 'Text box',
  number: 'Number',
  choice: 'List of options',
  image: 'Image from a list',
};

/** The kinds that fit a type. A number cannot be an image. A text cannot be a number box. */
export const kindsForType = (type: ParameterValueType): ParameterInputKind[] =>
  type === 'number' ? ['number', 'choice'] : ['text', 'choice', 'image'];

/** The changes to make when the kind changes. They clear the members that do not fit the new kind. */
export const patchForKind = (parameter: ParameterDeclaration, kind: ParameterInputKind): Partial<ParameterDeclaration> => {
  const patch: Partial<ParameterDeclaration> = {
    kind: kind === 'text' || kind === 'number' ? null : kind,
    options: kind === 'choice' ? parameter.options ?? [{ label: '', value: '' }] : null,
    allowedImages: kind === 'image' ? parameter.allowedImages ?? [] : null,
    min: kind === 'number' ? parameter.min ?? null : null,
    max: kind === 'number' ? parameter.max ?? null : null,
    unit: kind === 'number' ? parameter.unit ?? null : null,
    pattern: kind === 'number' ? parameter.pattern ?? null : null,
  };
  if (kind === 'choice' || kind === 'image') patch.default = null;
  return patch;
};

/**
 * One parameter of the editor as a card (feature 168): label, help text, name, type, kind, default,
 * required, and the settings of the chosen kind. The existing name checks and the move and remove
 * buttons stay.
 */
export const ParameterDeclarationCard: React.FC<ParameterDeclarationCardProps> = ({
  parameter,
  index,
  otherNames,
  count,
  touched,
  onTouched,
  onChange,
  onMove,
  onRemove,
  disabled,
}) => {
  const lastSuggested = useRef('');
  const kind = effectiveKind(parameter);
  const title = parameter.name || `parameter ${index + 1}`;
  const nameError = touched ? validateParameterName(parameter.name, otherNames) : undefined;
  const defaultError =
    parameter.type === 'number' && parameter.default != null && parameter.default !== '' && !/^-?\d+$/.test(parameter.default)
      ? 'A numeric default must be a whole number.'
      : undefined;

  const changeLabel = (label: string) => {
    const patch: Partial<ParameterDeclaration> = { label };
    // Suggest a name from the label until the author edits the name.
    if (parameter.name === '' || parameter.name === lastSuggested.current) {
      const suggestion = suggestParameterName(label, otherNames);
      lastSuggested.current = suggestion;
      patch.name = suggestion;
    }
    onChange(patch);
  };

  const changeType = (type: ParameterValueType) => {
    const valid = kindsForType(type);
    onChange(valid.includes(kind) ? { type } : { type, ...patchForKind({ ...parameter, type }, valid[0]) });
  };

  return (
    <li className="parameter-declaration-row parameter-declaration-card">
      <div className="parameter-declaration-fields">
        <label>
          <span>Label</span>
          <input
            type="text"
            value={parameter.label ?? ''}
            maxLength={80}
            disabled={disabled}
            placeholder="What the person sees"
            aria-label={`Parameter ${index + 1} label`}
            onChange={(e) => changeLabel(e.target.value)}
          />
        </label>

        <label>
          <span>Name</span>
          <input
            type="text"
            value={parameter.name}
            disabled={disabled}
            aria-invalid={nameError ? true : undefined}
            aria-label={`Parameter ${index + 1} name`}
            onBlur={onTouched}
            onChange={(e) => onChange({ name: e.target.value })}
          />
        </label>

        <label>
          <span>Type</span>
          <select
            value={parameter.type}
            disabled={disabled}
            aria-label={`Parameter ${index + 1} type`}
            onChange={(e) => changeType(e.target.value as ParameterValueType)}
          >
            <option value="text">Text</option>
            <option value="number">Number</option>
          </select>
        </label>

        <label>
          <span>Input</span>
          <select
            value={kind}
            disabled={disabled}
            aria-label={`Parameter ${index + 1} input`}
            onChange={(e) => onChange(patchForKind(parameter, e.target.value as ParameterInputKind))}
          >
            {kindsForType(parameter.type).map((k) => (
              <option key={k} value={k}>
                {KIND_LABELS[k]}
              </option>
            ))}
          </select>
        </label>

        <label>
          <span>Default</span>
          {kind === 'choice' ? (
            <select
              value={parameter.default ?? ''}
              disabled={disabled}
              aria-label={`Parameter ${index + 1} default`}
              onChange={(e) => onChange({ default: e.target.value === '' ? null : e.target.value })}
            >
              <option value="">(none)</option>
              {(parameter.options ?? []).filter((o) => o.value !== '').map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label || o.value}
                </option>
              ))}
            </select>
          ) : kind === 'image' ? (
            <select
              value={parameter.default ?? ''}
              disabled={disabled}
              aria-label={`Parameter ${index + 1} default`}
              onChange={(e) => onChange({ default: e.target.value === '' ? null : e.target.value })}
            >
              <option value="">(none)</option>
              {(parameter.allowedImages ?? []).map((id) => (
                <option key={id} value={id}>
                  {id}
                </option>
              ))}
            </select>
          ) : (
            <input
              type="text"
              value={parameter.default ?? ''}
              disabled={disabled}
              placeholder="(none)"
              aria-label={`Parameter ${index + 1} default`}
              onChange={(e) => onChange({ default: e.target.value === '' ? null : e.target.value })}
            />
          )}
        </label>

        <label className="parameter-declaration-required">
          <input
            type="checkbox"
            checked={parameter.required}
            disabled={disabled}
            aria-label={`Parameter ${index + 1} required`}
            onChange={(e) => onChange({ required: e.target.checked })}
          />
          <span>Required</span>
        </label>
      </div>

      {kind === 'choice' && (
        <OptionsEditor
          options={parameter.options ?? []}
          valueType={parameter.type}
          disabled={disabled}
          onChange={(options) => onChange({ options })}
        />
      )}
      {kind === 'image' && (
        <AllowedImagesEditor
          allowed={parameter.allowedImages ?? []}
          disabled={disabled}
          onChange={(allowedImages) => onChange({ allowedImages })}
        />
      )}
      {kind === 'number' && <NumberSettings parameter={parameter} disabled={disabled} onChange={onChange} />}

      <label className="parameter-declaration-description">
        <span>Help text</span>
        <input
          type="text"
          value={parameter.helpText ?? ''}
          maxLength={300}
          disabled={disabled}
          placeholder="Shown under the field"
          aria-label={`Parameter ${index + 1} help text`}
          onChange={(e) => onChange({ helpText: e.target.value === '' ? null : e.target.value })}
        />
      </label>

      <label className="parameter-declaration-description">
        <span>Description</span>
        <input
          type="text"
          value={parameter.description ?? ''}
          disabled={disabled}
          placeholder="Shown in the insert-parameter picker"
          aria-label={`Parameter ${index + 1} description`}
          onChange={(e) => onChange({ description: e.target.value })}
        />
      </label>

      <div className="parameter-declaration-actions">
        <button type="button" disabled={disabled || index === 0} aria-label={`Move ${title} up`} onClick={() => onMove(-1)}>
          ↑
        </button>
        <button type="button" disabled={disabled || index === count - 1} aria-label={`Move ${title} down`} onClick={() => onMove(1)}>
          ↓
        </button>
        <button type="button" disabled={disabled} aria-label={`Remove ${title}`} onClick={onRemove}>
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
          Required with no default: every queue that runs this must supply a value, or its start is refused.
        </p>
      )}
    </li>
  );
};
