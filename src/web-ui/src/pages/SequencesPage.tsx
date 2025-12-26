import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { MultiSelect, MultiOption } from '../components/MultiSelect';
import { listSequences, SequenceDto, createSequence, SequenceCreate, getSequence, updateSequence, deleteSequence } from '../services/sequences';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listCommands, CommandDto } from '../services/commands';
import { FormError, validateRequired } from '../components/Form';

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
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);

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
            const nameErr = validateRequired(name, 'Name');
            if (nameErr) {
              setError(nameErr);
              return;
            }
            const input: SequenceCreate = { name: name.trim(), steps: selectedCommands.length ? selectedCommands : [] };
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
          <FormError message={error} />
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
            const nameErr = validateRequired(editName, 'Name');
            if (nameErr) {
              setError(nameErr);
              return;
            }
            const input: SequenceCreate = { name: editName.trim(), steps: editSelectedCommands.length ? editSelectedCommands : undefined };
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
          <FormError message={error} />
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
            const data = await listSequences();
            const mapped: ListItem[] = data.map((s) => ({ id: s.id, name: s.name, details: { steps: s.steps?.length ?? 0 } }));
            setItems(mapped);
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: sequence is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setError(err?.message ?? 'Failed to delete sequence');
            }
          }
        }}
      />
    </section>
  );
};
