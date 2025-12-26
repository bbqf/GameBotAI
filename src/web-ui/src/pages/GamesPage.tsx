import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listGames, GameDto, createGame, GameCreate, getGame, updateGame, deleteGame } from '../services/games';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';

export const GamesPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [editName, setEditName] = useState('');
  const [deleteOpen, setDeleteOpen] = useState(false);

  useEffect(() => {
    let mounted = true;
    listGames()
      .then((data: GameDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((g) => ({
          id: g.id,
          name: g.name,
          details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
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
      <h2>Games</h2>
      <div className="actions-header">
        <button onClick={() => setCreating(true)}>Create Game</button>
      </div>
      {creating && (
        <form
          className="create-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: GameCreate = { name: name.trim() };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await createGame(input);
              setCreating(false);
              setName('');
              const data = await listGames();
              const mapped: ListItem[] = data.map((g) => ({
                id: g.id,
                name: g.name,
                details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to create game');
            }
          }}
        >
          <div>
            <label>Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} />
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
        emptyMessage="No games found."
        onSelect={async (id) => {
          setError(undefined);
          try {
            const g = await getGame(id);
            setEditingId(id);
            setEditName(g.name);
          } catch (err: any) {
            setError(err?.message ?? 'Failed to load game');
          }
        }}
      />
      {editingId && (
        <form
          className="edit-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: GameCreate = { name: editName.trim() };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await updateGame(editingId, input);
              setEditingId(undefined);
              const data = await listGames();
              const mapped: ListItem[] = data.map((g) => ({
                id: g.id,
                name: g.name,
                details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to update game');
            }
          }}
        >
          <h3>Edit Game</h3>
          <div>
            <label>Name</label>
            <input value={editName} onChange={(e) => setEditName(e.target.value)} />
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
            await deleteGame(editingId);
            setEditingId(undefined);
            const data = await listGames();
            const mapped: ListItem[] = data.map((g) => ({
              id: g.id,
              name: g.name,
              details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
            }));
            setItems(mapped);
          } catch (err: any) {
            const msg = err instanceof ApiError && err.status === 409 ? 'Cannot delete: game is referenced. Unlink or migrate before deleting.' : (err?.message ?? 'Failed to delete game');
            setError(msg);
          }
        }}
      />
    </section>
  );
};
