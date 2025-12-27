import React, { useEffect, useMemo, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listTriggers, TriggerDto, createTrigger, TriggerCreate, getTrigger, updateTrigger, deleteTrigger } from '../services/triggers';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listActions, ActionDto } from '../services/actions';
import { listCommands, CommandDto } from '../services/commands';
import { listSequences, SequenceDto } from '../services/sequences';
import { FormError, tryParseJson } from '../components/Form';
import { FormActions, FormSection } from '../components/unified/FormLayout';
import { SearchableDropdown, SearchableOption } from '../components/SearchableDropdown';
import { ReorderableList, ReorderableListItem } from '../components/ReorderableList';

type TriggerFormValue = {
  name: string;
  criteriaText: string;
  actions: string[];
  commands: string[];
  sequence?: string;
};

const emptyForm: TriggerFormValue = { name: '', criteriaText: '', actions: [], commands: [], sequence: undefined };

const toListItems = (ids: string[], options: SearchableOption[]): ReorderableListItem[] => {
  const lookup = new Map(options.map((o) => [o.value, o]));
  return ids.map((id) => {
    const opt = lookup.get(id);
    return { id, label: opt?.label ?? id, description: opt?.description };
  });
};

export const TriggersPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState<TriggerFormValue>(emptyForm);
  const [actionOptions, setActionOptions] = useState<SearchableOption[]>([]);
  const [commandOptions, setCommandOptions] = useState<SearchableOption[]>([]);
  const [sequenceOptions, setSequenceOptions] = useState<SearchableOption[]>([]);
  const [pendingActionId, setPendingActionId] = useState<string | undefined>(undefined);
  const [pendingCommandId, setPendingCommandId] = useState<string | undefined>(undefined);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    listTriggers()
      .then((data: TriggerDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((t) => ({
          id: t.id,
          name: t.name,
          details: {
            actions: t.actions?.length ?? 0,
            commands: t.commands?.length ?? 0,
            sequence: t.sequence ? 1 : 0
          }
        }));
        setItems(mapped);
      })
      .catch(() => setItems([]));
    Promise.all([listActions(), listCommands(), listSequences()])
      .then(([acts, cmds, seqs]: [ActionDto[], CommandDto[], SequenceDto[]]) => {
        if (!mounted) return;
        setActionOptions(acts.map((a) => ({ value: a.id, label: a.name, description: a.description })));
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
        setSequenceOptions(seqs.map((s) => ({ value: s.id, label: s.name })));
      })
      .catch(() => {
        if (!mounted) return;
        setActionOptions([]);
        setCommandOptions([]);
        setSequenceOptions([]);
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  const actionItems = useMemo(() => toListItems(form.actions, actionOptions), [form.actions, actionOptions]);
  const commandItems = useMemo(() => toListItems(form.commands, commandOptions), [form.commands, commandOptions]);

  const reloadTriggers = async () => {
    const data = await listTriggers();
    const mapped: ListItem[] = data.map((t) => ({
      id: t.id,
      name: t.name,
      details: { actions: t.actions?.length ?? 0, commands: t.commands?.length ?? 0, sequence: t.sequence ? 1 : 0 }
    }));
    setItems(mapped);
  };

  const resetForm = () => {
    setForm(emptyForm);
    setPendingActionId(undefined);
    setPendingCommandId(undefined);
    setErrors(undefined);
  };

  const validate = (v: TriggerFormValue): Record<string, string> | undefined => {
    const next: Record<string, string> = {};
    if (!v.name.trim()) next.name = 'Name is required';
    const parsed = tryParseJson<Record<string, unknown>>(v.criteriaText);
    if (parsed.error) next.criteria = parsed.error;
    return Object.keys(next).length ? next : undefined;
  };

  const buildPayload = (v: TriggerFormValue): { payload?: TriggerCreate; errors?: Record<string, string> } => {
    const validation = validate(v);
    if (validation) return { errors: validation };
    const parsed = tryParseJson<Record<string, unknown>>(v.criteriaText);
    if (parsed.error) return { errors: { criteria: parsed.error } };
    return {
      payload: {
        name: v.name.trim(),
        criteria: parsed.value,
        actions: v.actions.length ? v.actions : undefined,
        commands: v.commands.length ? v.commands : undefined,
        sequence: v.sequence || undefined,
      }
    };
  };

  const handleSubmit = async (mode: 'create' | 'edit') => {
    const result = buildPayload(form);
    if (result.errors) {
      setErrors(result.errors);
      return;
    }
    if (!result.payload) return;
    setSubmitting(true);
    try {
      if (mode === 'create') {
        await createTrigger(result.payload);
        setCreating(false);
        resetForm();
      } else {
        if (!editingId) return;
        await updateTrigger(editingId, result.payload);
      }
      await reloadTriggers();
    } catch (err: any) {
      setErrors({ form: err?.message ?? (mode === 'create' ? 'Failed to create trigger' : 'Failed to update trigger') });
    } finally {
      setSubmitting(false);
    }
  };

  const renderForm = (mode: 'create' | 'edit') => (
    <form
      className="edit-form"
      onSubmit={(e) => {
        e.preventDefault();
        handleSubmit(mode);
      }}
    >
      <FormSection title="Basics" description="Primary details for the trigger." id={mode === 'create' ? 'trigger-basics' : 'trigger-edit-basics'}>
        <div className="field">
          <label htmlFor={`${mode}-trigger-name`}>Name *</label>
          <input
            id={`${mode}-trigger-name`}
            value={form.name}
            onChange={(e) => { setForm({ ...form, name: e.target.value }); setErrors(undefined); }}
            aria-invalid={Boolean(errors?.name)}
            aria-describedby={errors?.name ? `${mode}-trigger-name-error` : undefined}
            disabled={submitting || loading}
          />
          {errors?.name && <div id={`${mode}-trigger-name-error`} className="field-error" role="alert">{errors.name}</div>}
        </div>
        <div className="field">
          <label htmlFor={`${mode}-trigger-criteria`}>Criteria (JSON)</label>
          <textarea
            id={`${mode}-trigger-criteria`}
            rows={6}
            value={form.criteriaText}
            onChange={(e) => { setForm({ ...form, criteriaText: e.target.value }); setErrors(undefined); }}
            aria-invalid={Boolean(errors?.criteria)}
            aria-describedby={errors?.criteria ? `${mode}-trigger-criteria-error` : undefined}
            disabled={submitting || loading}
          />
          {errors?.criteria && <div id={`${mode}-trigger-criteria-error`} className="field-error" role="alert">{errors.criteria}</div>}
        </div>
      </FormSection>

      <FormSection title="Actions" description="Select actions and arrange their order." id={`${mode}-trigger-actions`}>
        <SearchableDropdown
          id={`${mode}-trigger-action-dropdown`}
          label="Add action"
          options={actionOptions}
          value={pendingActionId}
          onChange={(val) => { setPendingActionId(val); setErrors(undefined); }}
          disabled={submitting || loading}
          placeholder="Select an action"
          onCreateNew={() => window.open('/actions/new', '_blank')}
          createLabel="Create new action"
        />
        <div className="field">
          <button type="button" onClick={() => {
            if (!pendingActionId || form.actions.includes(pendingActionId)) return;
            setForm({ ...form, actions: [...form.actions, pendingActionId] });
            setPendingActionId(undefined);
          }} disabled={submitting || loading || !pendingActionId}>Add to actions</button>
        </div>
        <ReorderableList
          items={actionItems}
          onChange={(next) => setForm({ ...form, actions: next.map((i) => i.id) })}
          onDelete={(item) => setForm({ ...form, actions: form.actions.filter((a) => a !== item.id) })}
          disabled={submitting || loading}
          emptyMessage="No actions selected yet."
        />
      </FormSection>

      <FormSection title="Commands" description="Select commands to run and set their order." id={`${mode}-trigger-commands`}>
        <SearchableDropdown
          id={`${mode}-trigger-command-dropdown`}
          label="Add command"
          options={commandOptions}
          value={pendingCommandId}
          onChange={(val) => { setPendingCommandId(val); setErrors(undefined); }}
          disabled={submitting || loading}
          placeholder="Select a command"
          onCreateNew={() => window.open('/commands/new', '_blank')}
          createLabel="Create new command"
        />
        <div className="field">
          <button type="button" onClick={() => {
            if (!pendingCommandId || form.commands.includes(pendingCommandId)) return;
            setForm({ ...form, commands: [...form.commands, pendingCommandId] });
            setPendingCommandId(undefined);
          }} disabled={submitting || loading || !pendingCommandId}>Add to commands</button>
        </div>
        <ReorderableList
          items={commandItems}
          onChange={(next) => setForm({ ...form, commands: next.map((i) => i.id) })}
          onDelete={(item) => setForm({ ...form, commands: form.commands.filter((c) => c !== item.id) })}
          disabled={submitting || loading}
          emptyMessage="No commands selected yet."
        />
      </FormSection>

      <FormSection title="Sequence" description="Optional sequence to execute." id={`${mode}-trigger-sequence`}>
        <SearchableDropdown
          id={`${mode}-trigger-sequence-dropdown`}
          label="Sequence"
          options={sequenceOptions}
          value={form.sequence}
          onChange={(val) => { setForm({ ...form, sequence: val }); setErrors(undefined); }}
          disabled={submitting || loading}
          placeholder="Select a sequence (optional)"
          onCreateNew={() => window.open('/sequences/new', '_blank')}
          createLabel="Create new sequence"
        />
      </FormSection>

      <FormActions
        submitting={submitting}
        onCancel={() => {
          if (mode === 'create') {
            setCreating(false);
          } else {
            setEditingId(undefined);
          }
          resetForm();
        }}
      >
        {loading && <span className="form-hint">Loading…</span>}
        {mode === 'edit' && (
          <button type="button" className="btn btn-danger" onClick={() => setDeleteOpen(true)} disabled={submitting}>Delete</button>
        )}
      </FormActions>
      <FormError message={errors?.form} />
    </form>
  );

  return (
    <section>
      <h2>Triggers</h2>
      <div className="actions-header">
        <button
          onClick={() => {
            setCreating(true);
            setEditingId(undefined);
            resetForm();
          }}
        >
          Create Trigger
        </button>
      </div>
      {creating && (
        renderForm('create')
      )}
      <List
        items={items}
        emptyMessage="No triggers found."
        onSelect={async (id) => {
          setErrors(undefined);
          try {
            const t = await getTrigger(id);
            setEditingId(id);
            setCreating(false);
            setForm({
              name: t.name,
              criteriaText: t.criteria ? JSON.stringify(t.criteria, null, 2) : '',
              actions: t.actions ?? [],
              commands: t.commands ?? [],
              sequence: t.sequence ?? undefined,
            });
            setPendingActionId(undefined);
            setPendingCommandId(undefined);
          } catch (err: any) {
            setErrors({ form: err?.message ?? 'Failed to load trigger' });
          }
        }}
      />
      {editingId && (
        <section>
          <h3>Edit Trigger</h3>
          {renderForm('edit')}
        </section>
      )}
      <ConfirmDeleteModal
        open={deleteOpen}
        itemName={form.name}
        message={deleteMessage}
        references={deleteReferences}
        onCancel={() => setDeleteOpen(false)}
        onConfirm={async () => {
          if (!editingId) return;
          try {
            await deleteTrigger(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            resetForm();
            await reloadTriggers();
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: trigger is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setErrors({ form: err?.message ?? 'Failed to delete trigger' });
            }
          }
        }}
      />
    </section>
  );
};
