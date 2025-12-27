import React, { useMemo, useState } from 'react';
import { FormActions, FormSection } from '../unified/FormLayout';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { ReorderableList, ReorderableListItem } from '../ReorderableList';

export type ParameterEntry = { id: string; key: string; value: string };

export type CommandFormValue = {
  name: string;
  parameters: ParameterEntry[];
  actions: string[];
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

export type CommandFormProps = {
  value: CommandFormValue;
  actionOptions: SearchableOption[];
  submitting?: boolean;
  loading?: boolean;
  errors?: Record<string, string>;
  onChange: (next: CommandFormValue) => void;
  onSubmit?: () => void;
  onCancel?: () => void;
  onCreateNewAction?: () => void;
};

const toListItems = (ids: string[], options: SearchableOption[]): ReorderableListItem[] => {
  const optionMap = new Map(options.map((o) => [o.value, o] as const));
  return ids.map((id) => {
    const match = optionMap.get(id);
    return {
      id,
      label: match?.label ?? id,
      description: match?.description
    };
  });
};

export const CommandForm: React.FC<CommandFormProps> = ({
  value,
  actionOptions,
  submitting,
  loading,
  errors,
  onChange,
  onSubmit,
  onCancel,
  onCreateNewAction,
}) => {
  const [pendingActionId, setPendingActionId] = useState<string | undefined>(undefined);

  const actionItems = useMemo(() => toListItems(value.actions, actionOptions), [value.actions, actionOptions]);

  const addAction = () => {
    if (!pendingActionId) return;
    if (value.actions.includes(pendingActionId)) return;
    onChange({ ...value, actions: [...value.actions, pendingActionId] });
    setPendingActionId(undefined);
  };

  const removeAction = (itemId: string) => {
    onChange({ ...value, actions: value.actions.filter((id) => id !== itemId) });
  };

  const updateActionOrder = (items: ReorderableListItem[]) => {
    onChange({ ...value, actions: items.map((it) => it.id) });
  };

  const updateParam = (index: number, field: 'key' | 'value', nextValue: string) => {
    const next = [...value.parameters];
    next[index] = { ...next[index], [field]: nextValue };
    onChange({ ...value, parameters: next });
  };

  const deleteParam = (index: number) => {
    const next = value.parameters.filter((_, i) => i !== index);
    onChange({ ...value, parameters: next });
  };

  const addParam = () => {
    const next = [...value.parameters, { id: makeId(), key: '', value: '' }];
    onChange({ ...value, parameters: next });
  };

  return (
    <form
      className="command-form"
      aria-label="Command form"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit?.();
      }}
    >
      <FormSection title="Basics" description="Primary details for the command." id="command-basics">
        <div className="field">
          <label htmlFor="command-name">Name *</label>
          <input
            id="command-name"
            value={value.name}
            onChange={(e) => onChange({ ...value, name: e.target.value })}
            aria-invalid={Boolean(errors?.name)}
            aria-describedby={errors?.name ? 'command-name-error' : undefined}
            disabled={submitting}
          />
          {errors?.name && <div id="command-name-error" className="field-error" role="alert">{errors.name}</div>}
        </div>
      </FormSection>

      <FormSection title="Parameters" description="Key/value parameters for the command." id="command-parameters" actions={
        <button type="button" onClick={addParam} disabled={submitting}>Add parameter</button>
      }>
        {value.parameters.length === 0 && <div className="empty-state">No parameters yet.</div>}
        {value.parameters.map((p, idx) => (
          <div className="field grid-2" key={p.id}>
            <div>
              <label htmlFor={`param-key-${p.id}`}>Key</label>
              <input
                id={`param-key-${p.id}`}
                value={p.key}
                onChange={(e) => updateParam(idx, 'key', e.target.value)}
                disabled={submitting}
              />
            </div>
            <div>
              <label htmlFor={`param-val-${p.id}`}>Value</label>
              <input
                id={`param-val-${p.id}`}
                value={p.value}
                onChange={(e) => updateParam(idx, 'value', e.target.value)}
                disabled={submitting}
              />
            </div>
            <div className="field-actions">
              <button type="button" onClick={() => deleteParam(idx)} disabled={submitting}>Delete</button>
            </div>
          </div>
        ))}
      </FormSection>

      <FormSection title="Actions" description="Choose actions and set their order." id="command-actions">
        <SearchableDropdown
          id="command-actions-dropdown"
          label="Add action"
          options={actionOptions}
          value={pendingActionId}
          onChange={setPendingActionId}
          onCreateNew={onCreateNewAction}
          disabled={submitting || loading}
          placeholder="Select an action"
          createLabel="Create new action"
        />
        <div className="field">
          <button type="button" onClick={addAction} disabled={submitting || !pendingActionId}>Add to list</button>
        </div>
        <ReorderableList
          items={actionItems}
          onChange={updateActionOrder}
          onDelete={(item) => removeAction(item.id)}
          disabled={submitting || loading}
          emptyMessage="No actions selected yet."
        />
        {errors?.actions && <div className="field-error" role="alert">{errors.actions}</div>}
      </FormSection>

      <FormActions submitting={submitting} onCancel={onCancel}>
        {loading && <span className="form-hint">Loading…</span>}
      </FormActions>
    </form>
  );
};
