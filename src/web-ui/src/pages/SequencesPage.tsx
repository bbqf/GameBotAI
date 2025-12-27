import React, { useEffect, useMemo, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listSequences, SequenceDto, createSequence, SequenceCreate, getSequence, updateSequence, deleteSequence } from '../services/sequences';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listCommands, CommandDto } from '../services/commands';
import { FormError } from '../components/Form';
import { FormActions, FormSection } from '../components/unified/FormLayout';
import { SearchableDropdown, SearchableOption } from '../components/SearchableDropdown';
import { ReorderableList, ReorderableListItem } from '../components/ReorderableList';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';

type SequenceStep = { id: string; commandId: string };

type SequenceFormValue = {
  name: string;
  steps: SequenceStep[];
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: SequenceFormValue = { name: '', steps: [] };

const toStepEntries = (ids?: string[]): SequenceStep[] => (ids ?? []).map((cmdId) => ({ id: makeId(), commandId: cmdId }));

const toPayloadSteps = (steps: SequenceStep[]) => steps.map((s) => s.commandId);

export const SequencesPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [commandOptions, setCommandOptions] = useState<SearchableOption[]>([]);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [form, setForm] = useState<SequenceFormValue>(emptyForm);
  const [pendingStepId, setPendingStepId] = useState<string | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(false);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    Promise.all([listSequences(), listCommands()])
      .then(([seqs, cmds]: [SequenceDto[], CommandDto[]]) => {
        if (!mounted) return;
        const mapped: ListItem[] = seqs.map((s) => ({
          id: s.id,
          name: s.name,
          details: { steps: s.steps?.length ?? 0 }
        }));
        setItems(mapped);
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
      })
      .catch(() => {
        if (!mounted) return;
        setItems([]);
        setCommandOptions([]);
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);

  const commandLookup = useMemo(() => new Map(commandOptions.map((o) => [o.value, o.label])), [commandOptions]);

  const reloadSequences = async () => {
    const data = await listSequences();
    const mapped: ListItem[] = data.map((s) => ({ id: s.id, name: s.name, details: { steps: s.steps?.length ?? 0 } }));
    setItems(mapped);
  };

  const resetForm = () => {
    setForm(emptyForm);
    setPendingStepId(undefined);
    setErrors(undefined);
    setDirty(false);
  };

  const stepItems = useMemo<ReorderableListItem[]>(() => {
    return form.steps.map((s, idx) => ({
      id: s.id,
      label: commandLookup.get(s.commandId) ?? s.commandId,
      description: `Step ${idx + 1}`
    }));
  }, [form.steps, commandLookup]);

  const validate = (v: SequenceFormValue): Record<string, string> | undefined => {
    const next: Record<string, string> = {};
    if (!v.name.trim()) next.name = 'Name is required';
    return Object.keys(next).length ? next : undefined;
  };

  return (
    <section>
      <h2>Sequences</h2>
      <div className="actions-header">
        <button
          onClick={() => {
            if (!confirmNavigate()) return;
            setCreating(true);
            setEditingId(undefined);
            setErrors(undefined);
            resetForm();
          }}
        >
          Create Sequence
        </button>
      </div>
      {creating && (
        <form
          className="edit-form"
          onSubmit={async (e) => {
            e.preventDefault();
            const validation = validate(form);
            if (validation) {
              setErrors(validation);
              return;
            }
            setSubmitting(true);
            try {
              await createSequence({ name: form.name.trim(), steps: toPayloadSteps(form.steps) });
              setCreating(false);
              setForm(emptyForm);
              setPendingStepId(undefined);
              setDirty(false);
              await reloadSequences();
            } catch (err: any) {
              setErrors({ form: err?.message ?? 'Failed to create sequence' });
            } finally {
              setSubmitting(false);
            }
          }}
        >
          <FormSection title="Basics" description="Primary details for the sequence." id="sequence-basics">
            <div className="field">
              <label htmlFor="sequence-name">Name *</label>
              <input
                id="sequence-name"
                value={form.name}
                onChange={(e) => { setErrors(undefined); setForm({ ...form, name: e.target.value }); setDirty(true); }}
                aria-invalid={Boolean(errors?.name)}
                aria-describedby={errors?.name ? 'sequence-name-error' : undefined}
                disabled={submitting || loading}
              />
              {errors?.name && <div id="sequence-name-error" className="field-error" role="alert">{errors.name}</div>}
            </div>
          </FormSection>

          <FormSection title="Steps" description="Add commands in the order they should run." id="sequence-steps">
            <SearchableDropdown
              id="sequence-step-dropdown"
              label="Add command"
              options={commandOptions}
              value={pendingStepId}
              onChange={(val) => { setPendingStepId(val); setErrors(undefined); }}
              disabled={submitting || loading}
              placeholder="Select a command"
              onCreateNew={() => window.open('/commands/new', '_blank')}
              createLabel="Create new command"
            />
            <div className="field">
              <button type="button" onClick={() => {
                if (!pendingStepId) return;
                const next = [...form.steps, { id: makeId(), commandId: pendingStepId }];
                setForm({ ...form, steps: next });
                setPendingStepId(undefined);
                setDirty(true);
              }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
            </div>
            <ReorderableList
              items={stepItems}
              onChange={(next) => {
                const mapped = next.map((item, idx) => ({ id: item.id, commandId: form.steps.find((s) => s.id === item.id)?.commandId ?? form.steps[idx]?.commandId ?? '' }));
                setForm({ ...form, steps: mapped.filter((s) => s.commandId) });
                setDirty(true);
              }}
              onDelete={(item) => {
                setForm({ ...form, steps: form.steps.filter((s) => s.id !== item.id) });
                setDirty(true);
              }}
              disabled={submitting || loading}
              emptyMessage="No commands added yet."
            />
            <div className="form-hint">Steps execute in listed order; drag buttons to reorder before saving.</div>
          </FormSection>

          <FormActions submitting={submitting} onCancel={() => { if (!confirmNavigate()) return; setCreating(false); resetForm(); }}>
            {loading && <span className="form-hint">Loading…</span>}
          </FormActions>
          <FormError message={errors?.form} />
        </form>
      )}
      <List
        items={items}
        emptyMessage="No sequences found."
        onSelect={async (id) => {
          if (!confirmNavigate()) return;
          setErrors(undefined);
          try {
            const s = await getSequence(id);
            setEditingId(id);
            setCreating(false);
            setPendingStepId(undefined);
            setForm({ name: s.name, steps: toStepEntries(s.steps) });
            setDirty(false);
          } catch (err: any) {
            setErrors({ form: err?.message ?? 'Failed to load sequence' });
          }
        }}
      />
      {editingId && (
        <section>
          <h3>Edit Sequence</h3>
          <form
            className="edit-form"
            onSubmit={async (e) => {
              e.preventDefault();
              if (!editingId) return;
              const validation = validate(form);
              if (validation) {
                setErrors(validation);
                return;
              }
              setSubmitting(true);
              try {
                await updateSequence(editingId, { name: form.name.trim(), steps: toPayloadSteps(form.steps) });
                await reloadSequences();
                setDirty(false);
              } catch (err: any) {
                setErrors({ form: err?.message ?? 'Failed to update sequence' });
              } finally {
                setSubmitting(false);
              }
            }}
          >
            <FormSection title="Basics" description="Primary details for the sequence." id="sequence-edit-basics">
              <div className="field">
                <label htmlFor="sequence-edit-name">Name *</label>
                <input
                  id="sequence-edit-name"
                  value={form.name}
                  onChange={(e) => { setErrors(undefined); setForm({ ...form, name: e.target.value }); setDirty(true); }}
                  aria-invalid={Boolean(errors?.name)}
                  aria-describedby={errors?.name ? 'sequence-edit-name-error' : undefined}
                  disabled={submitting || loading}
                />
                {errors?.name && <div id="sequence-edit-name-error" className="field-error" role="alert">{errors.name}</div>}
              </div>
            </FormSection>

            <FormSection title="Steps" description="Add commands in the order they should run." id="sequence-edit-steps">
              <SearchableDropdown
                id="sequence-edit-step-dropdown"
                label="Add command"
                options={commandOptions}
                value={pendingStepId}
                onChange={(val) => { setPendingStepId(val); setErrors(undefined); }}
                disabled={submitting || loading}
                placeholder="Select a command"
                onCreateNew={() => window.open('/commands/new', '_blank')}
                createLabel="Create new command"
              />
              <div className="field">
                <button type="button" onClick={() => {
                  if (!pendingStepId) return;
                  const next = [...form.steps, { id: makeId(), commandId: pendingStepId }];
                  setForm({ ...form, steps: next });
                  setPendingStepId(undefined);
                  setDirty(true);
                }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
              </div>
              <ReorderableList
                items={stepItems}
                onChange={(next) => {
                  const mapped = next.map((item, idx) => ({ id: item.id, commandId: form.steps.find((s) => s.id === item.id)?.commandId ?? form.steps[idx]?.commandId ?? '' }));
                  setForm({ ...form, steps: mapped.filter((s) => s.commandId) });
                  setDirty(true);
                }}
                onDelete={(item) => {
                  setForm({ ...form, steps: form.steps.filter((s) => s.id !== item.id) });
                  setDirty(true);
                }}
                disabled={submitting || loading}
                emptyMessage="No commands added yet."
              />
              <div className="form-hint">Use Move up/down to set the execution order before saving.</div>
            </FormSection>

            <FormActions
              submitting={submitting}
              onCancel={() => {
                if (!confirmNavigate()) return;
                setEditingId(undefined);
                resetForm();
              }}
            >
              {loading && <span className="form-hint">Loading…</span>}
              <button type="button" className="btn btn-danger" onClick={() => setDeleteOpen(true)} disabled={submitting}>Delete</button>
            </FormActions>
            <FormError message={errors?.form} />
          </form>
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
            await deleteSequence(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            setDirty(false);
            resetForm();
            await reloadSequences();
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: sequence is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setErrors({ form: err?.message ?? 'Failed to delete sequence' });
            }
          }
        }}
      />
    </section>
  );
};
