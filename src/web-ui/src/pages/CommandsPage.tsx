import React, { useEffect, useMemo, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listCommands, CommandDto, createCommand, getCommand, updateCommand, deleteCommand } from '../services/commands';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listActions, ActionDto } from '../services/actions';
import { CommandForm, CommandFormValue, ParameterEntry } from '../components/commands/CommandForm';
import { SearchableOption } from '../components/SearchableDropdown';

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const paramsFromDto = (obj?: Record<string, unknown>): ParameterEntry[] => {
  if (!obj) return [];
  return Object.entries(obj).map(([key, val]) => ({ id: makeId(), key, value: String(val ?? '') }));
};

const paramsToObject = (entries: ParameterEntry[]): Record<string, unknown> | undefined => {
  const result: Record<string, unknown> = {};
  for (const p of entries) {
    if (!p.key.trim()) continue;
    result[p.key.trim()] = p.value;
  }
  return Object.keys(result).length ? result : undefined;
};

const emptyForm: CommandFormValue = { name: '', parameters: [], actions: [] };

export const CommandsPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState<CommandFormValue>(emptyForm);
  const [actionOptions, setActionOptions] = useState<SearchableOption[]>([]);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [loadingCommands, setLoadingCommands] = useState(false);

  useEffect(() => {
    let mounted = true;
    setLoadingCommands(true);
    Promise.all([listCommands(), listActions()])
      .then(([cmds, acts]: [CommandDto[], ActionDto[]]) => {
        if (!mounted) return;
        const mapped: ListItem[] = cmds.map((c) => ({
          id: c.id,
          name: c.name,
          details: { actions: c.actions?.length ?? 0 }
        }));
        setItems(mapped);
        setActionOptions(acts.map((a) => ({ value: a.id, label: a.name, description: a.description })));
      })
      .catch(() => {
        if (!mounted) return;
        setItems([]);
        setActionOptions([]);
      })
      .finally(() => {
        if (mounted) setLoadingCommands(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  const actionLookup = useMemo(() => new Map(actionOptions.map((o) => [o.value, o.label])), [actionOptions]);

  const reloadCommands = async () => {
    const data = await listCommands();
    const mapped: ListItem[] = data.map((c) => ({
      id: c.id,
      name: c.name,
      details: { actions: c.actions?.length ?? 0 }
    }));
    setItems(mapped);
  };

  const validate = (v: CommandFormValue): Record<string, string> | undefined => {
    const next: Record<string, string> = {};
    if (!v.name.trim()) next.name = 'Name is required';
    return Object.keys(next).length ? next : undefined;
  };

  return (
    <section>
      <h2>Commands</h2>
      <div className="actions-header">
        <button onClick={() => { setCreating(true); setEditingId(undefined); setForm(emptyForm); setErrors(undefined); }}>Create Command</button>
      </div>
      {creating && (
        <CommandForm
          value={form}
          actionOptions={actionOptions}
          errors={errors}
          submitting={submitting}
          loading={loadingCommands}
          onCreateNewAction={() => window.open('/actions/new', '_blank')}
          onChange={(v) => { setErrors(undefined); setForm(v); }}
          onCancel={() => { setCreating(false); setForm(emptyForm); setErrors(undefined); }}
          onSubmit={async () => {
            const validation = validate(form);
            if (validation) {
              setErrors(validation);
              return;
            }
            setSubmitting(true);
            try {
              await createCommand({ name: form.name.trim(), parameters: paramsToObject(form.parameters), actions: form.actions.length ? form.actions : undefined });
              setCreating(false);
              setForm(emptyForm);
              await reloadCommands();
            } catch (err: any) {
              setErrors({ form: err?.message ?? 'Failed to create command' });
            } finally {
              setSubmitting(false);
            }
          }}
        />
      )}
      <List
        items={items}
        emptyMessage="No commands found."
        onSelect={async (id) => {
          setErrors(undefined);
          try {
            const c = await getCommand(id);
            setEditingId(id);
            setCreating(false);
            setForm({
              name: c.name,
              parameters: paramsFromDto(c.parameters),
              actions: c.actions ?? []
            });
          } catch (err: any) {
            setErrors({ form: err?.message ?? 'Failed to load command' });
          }
        }}
      />
      {editingId && (
        <section>
          <h3>Edit Command</h3>
          <CommandForm
            value={form}
            actionOptions={actionOptions}
            errors={errors}
            submitting={submitting}
            loading={loadingCommands}
            onCreateNewAction={() => window.open('/actions/new', '_blank')}
            onChange={(v) => { setErrors(undefined); setForm(v); }}
            onCancel={() => { setEditingId(undefined); setForm(emptyForm); setErrors(undefined); }}
            onSubmit={async () => {
              if (!editingId) return;
              const validation = validate(form);
              if (validation) {
                setErrors(validation);
                return;
              }
              setSubmitting(true);
              try {
                await updateCommand(editingId, { name: form.name.trim(), parameters: paramsToObject(form.parameters), actions: form.actions.length ? form.actions : undefined });
                await reloadCommands();
              } catch (err: any) {
                setErrors({ form: err?.message ?? 'Failed to update command' });
              } finally {
                setSubmitting(false);
              }
            }}
          />
          <div className="form-actions">
            <button type="button" className="btn btn-danger" onClick={() => setDeleteOpen(true)}>Delete</button>
          </div>
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
            await deleteCommand(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            await reloadCommands();
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: command is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setErrors({ form: err?.message ?? 'Failed to delete command' });
            }
          }
        }}
      />
    </section>
  );
};
