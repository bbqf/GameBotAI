import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listGames, GameDto, createGame, GameCreate } from '../services/games';

export const GamesPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);

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
      <List items={items} emptyMessage="No games found." />
    </section>
  );
};
