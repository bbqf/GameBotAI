import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { MultiSelect, MultiOption } from '../components/MultiSelect';
import { listSequences, SequenceDto, createSequence, SequenceCreate, getSequence, updateSequence, deleteSequence } from '../services/sequences';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listCommands, CommandDto } from '../services/commands';

export const SequencesPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [commandOptions, setCommandOptions] = useState<MultiOption[]>([]);
  const [selectedCommands, setSelectedCommands] = useState<string[]>([]);
  const [error, setError] = useState<string | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [editName, setEditName] = useState('');
  const [editSelectedCommands, setEditSelectedCommands] = useState<string[]>([]);
  const [deleteOpen, setDeleteOpen] = useState(false);

  useEffect(() => {
    let mounted = true;
    listSequences()
      .then((data: SequenceDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((s) => ({
          id: s.id,
          name: s.name,
          details: { steps: s.steps?.length ?? 0 }
        }));
        setItems(mapped);
      })
      .catch(() => setItems([]));
    listCommands()
      .then((cmds: CommandDto[]) => {
        if (!mounted) return;
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
      })
      .catch(() => setCommandOptions([]));
    return () => {
      mounted = false;
    };
  }, []);

  return (
    <section>
      <h2>Sequences</h2>
      <div className="actions-header">
        <button onClick={() => setCreating(true)}>Create Sequence</button>
      </div>
      {creating && (
        <form
          className="create-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: SequenceCreate = { name: name.trim(), steps: selectedCommands.length ? selectedCommands : [] };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await createSequence(input);
              setCreating(false);
              setName('');
              setSelectedCommands([]);
              const data = await listSequences();
              const mapped: ListItem[] = data.map((s) => ({
                id: s.id,
                name: s.name,
                details: { steps: s.steps?.length ?? 0 }
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to create sequence');
            }
          }}
        >
          <div>
            <label>Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <MultiSelect label="Commands (steps)" values={selectedCommands} options={commandOptions} onChange={setSelectedCommands} />
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
        emptyMessage="No sequences found."
        onSelect={async (id) => {
          setError(undefined);
          try {
            const s = await getSequence(id);
            setEditingId(id);
            setEditName(s.name);
            setEditSelectedCommands(s.steps ?? []);
          } catch (err: any) {
            setError(err?.message ?? 'Failed to load sequence');
          }
        }}
      />
      {editingId && (
        <form
          className="edit-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: SequenceCreate = { name: editName.trim(), steps: editSelectedCommands.length ? editSelectedCommands : undefined };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await updateSequence(editingId, input);
              setEditingId(undefined);
              const data = await listSequences();
              const mapped: ListItem[] = data.map((s) => ({ id: s.id, name: s.name, details: { steps: s.steps?.length ?? 0 } }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to update sequence');
            }
          }}
        >
          <h3>Edit Sequence</h3>
          <div>
            <label>Name</label>
            <input value={editName} onChange={(e) => setEditName(e.target.value)} />
          </div>
          <div>
            <MultiSelect label="Commands" values={editSelectedCommands} options={commandOptions} onChange={setEditSelectedCommands} />
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
            await deleteSequence(editingId);
            setEditingId(undefined);
            const data = await listSequences();
            const mapped: ListItem[] = data.map((s) => ({ id: s.id, name: s.name, details: { steps: s.steps?.length ?? 0 } }));
            setItems(mapped);
          } catch (err: any) {
            const msg = err instanceof ApiError && err.status === 409 ? 'Cannot delete: sequence is referenced. Unlink or migrate before deleting.' : (err?.message ?? 'Failed to delete sequence');
            setError(msg);
          }
        }}
      />
    </section>
  );
};
