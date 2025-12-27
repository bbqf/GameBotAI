import React from 'react';
import { AttributeDefinition } from '../../types/actions';

export type FieldRendererProps = {
  definition: AttributeDefinition;
  value: unknown;
  error?: string;
  onChange: (key: string, value: unknown) => void;
};

const coerce = (def: AttributeDefinition, raw: string | boolean): unknown => {
  if (def.dataType === 'boolean') return Boolean(raw);
  if (def.dataType === 'number') {
    const num = Number(raw);
    return Number.isNaN(num) ? raw : num;
  }
  return raw;
};

export const FieldRenderer: React.FC<FieldRendererProps> = ({ definition, value, error, onChange }) => {
  const id = `field-${definition.key}`;
  const helpId = definition.helpText ? `${id}-help` : undefined;

  const commonLabel = (
    <label htmlFor={id}>
      {definition.label}
      {definition.required ? ' *' : ''}
    </label>
  );

  const help = definition.helpText ? (
    <div id={helpId} className="field-help">
      {definition.helpText}
    </div>
  ) : null;

  const errorNode = error ? (
    <div className="field-error" role="alert">
      {error}
    </div>
  ) : null;

  if (definition.dataType === 'boolean') {
    const boolVal = Boolean(value);
    return (
      <div className="field">
        <label className="checkbox">
          <input
            id={id}
            type="checkbox"
            checked={boolVal}
            aria-describedby={helpId}
            onChange={(e) => onChange(definition.key, e.target.checked)}
          />
          <span>{definition.label}{definition.required ? ' *' : ''}</span>
        </label>
        {help}
        {errorNode}
      </div>
    );
  }

  if (definition.dataType === 'enum') {
    const opts = definition.constraints?.allowedValues ?? [];
    const stringVal = typeof value === 'string' ? value : '';
    return (
      <div className="field">
        {commonLabel}
        <select
          id={id}
          aria-describedby={helpId}
          value={stringVal}
          onChange={(e) => onChange(definition.key, e.target.value)}
        >
          <option value="" disabled>{definition.required ? 'Select an option' : 'Select (optional)'}</option>
          {opts.map((opt) => (
            <option key={opt} value={opt}>{opt}</option>
          ))}
        </select>
        {help}
        {errorNode}
      </div>
    );
  }

  const isNumber = definition.dataType === 'number';
  const inputValue = value === undefined || value === null ? '' : String(value);

  return (
    <div className="field">
      {commonLabel}
      <input
        id={id}
        type={isNumber ? 'number' : 'text'}
        aria-describedby={helpId}
        value={inputValue}
        onChange={(e) => onChange(definition.key, coerce(definition, isNumber ? e.target.value : e.target.value))}
      />
      {help}
      {errorNode}
    </div>
  );
};
