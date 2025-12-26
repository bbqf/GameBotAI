import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { Dropdown } from '../components/Dropdown';
import { MultiSelect, MultiOption } from '../components/MultiSelect';
import { listCommands, CommandDto, createCommand, CommandCreate, getCommand, updateCommand, deleteCommand } from '../services/commands';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listActions, ActionDto } from '../services/actions';

export const CommandsPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [parametersText, setParametersText] = useState('');
  const [actionOptions, setActionOptions] = useState<MultiOption[]>([]);
  const [selectedActions, setSelectedActions] = useState<string[]>([]);
  const [error, setError] = useState<string | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [editName, setEditName] = useState('');
  const [editParametersText, setEditParametersText] = useState('');
  const [editSelectedActions, setEditSelectedActions] = useState<string[]>([]);
  const [deleteOpen, setDeleteOpen] = useState(false);

  useEffect(() => {
    let mounted = true;
    listCommands()
      .then((data: CommandDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((c) => ({
          id: c.id,
          name: c.name,
          details: { actions: c.actions?.length ?? 0 }
        }));
        setItems(mapped);
      })
      .catch(() => setItems([]));
    listActions()
      .then((acts: ActionDto[]) => {
        if (!mounted) return;
        setActionOptions(acts.map((a) => ({ value: a.id, label: a.name })));
      })
      .catch(() => setActionOptions([]));
    return () => {
      mounted = false;
    };
  }, []);

  return (
    <section>
      <h2>Commands</h2>
      <div className="actions-header">
        <button onClick={() => setCreating(true)}>Create Command</button>
      </div>
      {creating && (
        <form
          className="create-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            let parameters: Record<string, unknown> | undefined = undefined;
            if (parametersText.trim().length > 0) {
              try {
                parameters = JSON.parse(parametersText);
              } catch {
                setError('Parameters must be valid JSON');
                return;
              }
            }
            const input: CommandCreate = { name: name.trim(), parameters, actions: selectedActions.length ? selectedActions : undefined };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await createCommand(input);
              setCreating(false);
              setName('');
              setParametersText('');
              setSelectedActions([]);
              const data = await listCommands();
              const mapped: ListItem[] = data.map((c) => ({
                id: c.id,
                name: c.name,
                details: { actions: c.actions?.length ?? 0 }
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to create command');
            }
          }}
        >
          <div>
            <label>Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <label>Parameters (JSON)</label>
            <textarea rows={4} value={parametersText} onChange={(e) => setParametersText(e.target.value)} />
          </div>
          <div>
            <MultiSelect label="Actions" values={selectedActions} options={actionOptions} onChange={setSelectedActions} />
          </div>
          {error && <div className="form-error" role="alert">{error}</div>}
          <div className="form-actions">
            <button type="submit">Create</button>
            <button type="button" onClick={() => setCreating(false)}>Cancel</button>
          </div>
        </form>
      )}
      <List
        items={items}
        emptyMessage="No commands found."
        onSelect={async (id) => {
          setError(undefined);
          try {
            const c = await getCommand(id);
            setEditingId(id);
            setEditName(c.name);
            setEditParametersText(c.parameters ? JSON.stringify(c.parameters, null, 2) : '');
            setEditSelectedActions(c.actions ?? []);
          } catch (err: any) {
            setError(err?.message ?? 'Failed to load command');
          }
        }}
      />
      {editingId && (
        <form
          className="edit-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            let parameters: Record<string, unknown> | undefined = undefined;
            if (editParametersText.trim().length > 0) {
              try {
                parameters = JSON.parse(editParametersText);
              } catch {
                setError('Parameters must be valid JSON');
                return;
              }
            }
            const input: CommandCreate = { name: editName.trim(), parameters, actions: editSelectedActions.length ? editSelectedActions : undefined };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await updateCommand(editingId, input);
              setEditingId(undefined);
              const data = await listCommands();
              const mapped: ListItem[] = data.map((c) => ({
                id: c.id,
                name: c.name,
                details: { actions: c.actions?.length ?? 0 }
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to update command');
            }
          }}
        >
          <h3>Edit Command</h3>
          <div>
            <label>Name</label>
            <input value={editName} onChange={(e) => setEditName(e.target.value)} />
          </div>
          <div>
            <label>Parameters (JSON)</label>
            <textarea rows={4} value={editParametersText} onChange={(e) => setEditParametersText(e.target.value)} />
          </div>
          <div>
            <MultiSelect label="Actions" values={editSelectedActions} options={actionOptions} onChange={setEditSelectedActions} />
          </div>
          {error && <div className="form-error" role="alert">{error}</div>}
          <div className="form-actions">
            <button type="submit">Save</button>
            <button type="button" onClick={() => setEditingId(undefined)}>Cancel</button>
            <button type="button" className="btn btn-danger" onClick={() => setDeleteOpen(true)}>Delete</button>
          </div>
        </form>
      )}
      <ConfirmDeleteModal
        open={deleteOpen}
        itemName={editName}
        onCancel={() => setDeleteOpen(false)}
        onConfirm={async () => {
          setDeleteOpen(false);
          if (!editingId) return;
          try {
            await deleteCommand(editingId);
            setEditingId(undefined);
            const data = await listCommands();
            const mapped: ListItem[] = data.map((c) => ({
              id: c.id,
              name: c.name,
              details: { actions: c.actions?.length ?? 0 }
            }));
            setItems(mapped);
          } catch (err: any) {
            const msg = err instanceof ApiError && err.status === 409 ? 'Cannot delete: command is referenced. Unlink or migrate before deleting.' : (err?.message ?? 'Failed to delete command');
            setError(msg);
          }
        }}
      />
    </section>
  );
};
