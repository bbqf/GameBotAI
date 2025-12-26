import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listActions, ActionDto, createAction, ActionCreate } from '../services/actions';

export const ActionsPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);

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

  return (
    <section>
      <h2>Actions</h2>
      <div className="actions-header">
        <button onClick={() => setCreating(true)}>Create Action</button>
      </div>
      {creating && (
        <form
          className="create-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: ActionCreate = { name: name.trim(), description: description.trim() || undefined };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await createAction(input);
              setCreating(false);
              setName('');
              setDescription('');
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
            <label>Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <label>Description</label>
            <input value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          {error && <div className="form-error" role="alert">{error}</div>}
          <div className="form-actions">
            <button type="submit">Create</button>
            <button type="button" onClick={() => setCreating(false)}>Cancel</button>
          </div>
        </form>
      )}
      <List items={items} emptyMessage="No actions found." />
    </section>
  );
};
