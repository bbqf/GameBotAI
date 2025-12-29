import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listActions, ActionDto, createAction, ActionCreate, getAction, updateAction, deleteAction } from '../services/actions';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { validateRequired, FormError } from '../components/Form';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';

export const ActionsPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [editName, setEditName] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [dirty, setDirty] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);

  useEffect(() => {
    let mounted = true;
    listActions()
      .then((data: ActionDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((a) => ({
          id: a.id,
          name: a.name,
          details: a.description ? { description: a.description } : undefined
        }));
        setItems(mapped);
      })
      .catch(() => setItems([]));
    return () => {
      mounted = false;
    };
  }, []);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);

  return (
    <section>
      <h2>Actions</h2>
      <div className="actions-header">
        <button onClick={() => { if (!confirmNavigate()) return; setCreating(true); setEditingId(undefined); setDirty(false); }}>Create Action</button>
      </div>
      {creating && (
        <form
          className="create-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: ActionCreate = { name: name.trim(), description: description.trim() || undefined };
            {
              const errMsg = validateRequired(input.name, 'Name');
              if (errMsg) { setError(errMsg); return; }
            }
            try {
              await createAction(input);
              setCreating(false);
              setName('');
              setDescription('');
              setDirty(false);
              const data = await listActions();
              const mapped: ListItem[] = data.map((a) => ({
                id: a.id,
                name: a.name,
                details: a.description ? { description: a.description } : undefined
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to create action');
            }
          }}
        >
          <div>
            <label htmlFor="create-action-name">Name</label>
            <input id="create-action-name" value={name} onChange={(e) => { setDirty(true); setName(e.target.value); }} />
          </div>
          <div>
            <label htmlFor="create-action-description">Description</label>
            <input id="create-action-description" value={description} onChange={(e) => { setDirty(true); setDescription(e.target.value); }} />
          </div>
          <FormError message={error} />
          <div className="form-actions">
            <button type="submit">Create</button>
            <button type="button" onClick={() => { if (!confirmNavigate()) return; setCreating(false); setName(''); setDescription(''); setDirty(false); }}>Cancel</button>
          </div>
        </form>
      )}
      <List
        items={items}
        emptyMessage="No actions found."
        onSelect={async (id) => {
          if (!confirmNavigate()) return;
          setError(undefined);
          try {
            const a = await getAction(id);
            setEditingId(id);
            setEditName(a.name);
            setEditDescription(a.description ?? '');
            setDirty(false);
          } catch (err: any) {
            setError(err?.message ?? 'Failed to load action');
          }
        }}
      />
      {editingId && (
        <form
          className="edit-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: ActionCreate = { name: editName.trim(), description: editDescription.trim() || undefined };
            {
              const errMsg = validateRequired(input.name, 'Name');
              if (errMsg) { setError(errMsg); return; }
            }
            try {
              await updateAction(editingId, input);
              setEditingId(undefined);
              setDirty(false);
              const data = await listActions();
              const mapped: ListItem[] = data.map((a) => ({
                id: a.id,
                name: a.name,
                details: a.description ? { description: a.description } : undefined
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to update action');
            }
          }}
        >
          <h3>Edit Action</h3>
          <div>
            <label htmlFor="edit-action-name">Name</label>
            <input id="edit-action-name" value={editName} onChange={(e) => { setDirty(true); setEditName(e.target.value); }} />
          </div>
          <div>
            <label htmlFor="edit-action-description">Description</label>
            <input id="edit-action-description" value={editDescription} onChange={(e) => { setDirty(true); setEditDescription(e.target.value); }} />
          </div>
          <FormError message={error} />
          <div className="form-actions">
            <button type="submit">Save</button>
            <button type="button" onClick={() => { if (!confirmNavigate()) return; setEditingId(undefined); setDirty(false); }}>Cancel</button>
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
            await deleteAction(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            setDirty(false);
            const data = await listActions();
            const mapped: ListItem[] = data.map((a) => ({
              id: a.id,
              name: a.name,
              details: a.description ? { description: a.description } : undefined
            }));
            setItems(mapped);
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: action is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setError(err?.message ?? 'Failed to delete action');
            }
          }
        }}
      />
    </section>
  );
};
