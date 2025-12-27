import React from 'react';
import { ActionType, ValidationMessage } from '../../types/actions';
import { FieldRenderer } from './FieldRenderer';

export type ActionFormValue = {
  name: string;
  type: string;
  attributes: Record<string, unknown>;
};

export type ActionFormProps = {
  actionTypes: ActionType[];
  value: ActionFormValue;
  errors?: ValidationMessage[];
  loading?: boolean;
  submitting?: boolean;
  onChange: (next: ActionFormValue) => void;
  onSubmit?: () => void;
  onCancel?: () => void;
};

const getFieldError = (errors: ValidationMessage[] | undefined, field: string): string | undefined => {
  if (!errors) return undefined;
  const match = errors.find((e) => e.field === field);
  return match?.message;
};

export const ActionForm: React.FC<ActionFormProps> = ({
  actionTypes,
  value,
  errors,
  loading,
  submitting,
  onChange,
  onSubmit,
  onCancel
}) => {
  const selectedType = actionTypes.find((t) => t.key === value.type);

  const updateAttributes = (key: string, newValue: unknown) => {
    const next = { ...value.attributes };
    if (newValue === undefined || newValue === '') {
      delete next[key];
    } else {
      next[key] = newValue;
    }
    onChange({ ...value, attributes: next });
  };

  const renderFields = () => {
    if (!selectedType) return null;
    return selectedType.attributeDefinitions.map((def) => (
      <FieldRenderer
        key={def.key}
        definition={def}
        value={value.attributes[def.key]}
        error={getFieldError(errors, def.key)}
        onChange={updateAttributes}
      />
    ));
  };

  return (
    <form
      className="action-form"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit?.();
      }}
    >
      <div className="field">
        <label htmlFor="action-name">Name *</label>
        <input
          id="action-name"
          value={value.name}
          onChange={(e) => onChange({ ...value, name: e.target.value })}
        />
      </div>

      <div className="field">
        <label htmlFor="action-type">Action Type *</label>
        <select
          id="action-type"
          value={value.type}
          onChange={(e) => onChange({ ...value, type: e.target.value, attributes: {} })}
        >
          <option value="" disabled>Select an action type</option>
          {actionTypes.map((t) => (
            <option key={t.key} value={t.key}>{t.displayName}</option>
          ))}
        </select>
      </div>

      {loading && <div className="form-hint">Loading definitions...</div>}
      {!loading && renderFields()}

      {errors && errors.length > 0 && (
        <div className="form-error" role="alert">
          {errors.filter((e) => !e.field).map((e, idx) => (<div key={idx}>{e.message}</div>))}
        </div>
      )}

      <div className="form-actions">
        <button type="submit" disabled={submitting}>Save</button>
        {onCancel && (
          <button type="button" onClick={onCancel}>Cancel</button>
        )}
      </div>
    </form>
  );
};
