import React, { useEffect, useMemo, useState } from 'react';
import { listGames, GameDto, createGame, getGame, updateGame, deleteGame } from '../services/games';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { FormError } from '../components/Form';
import { FormActions, FormSection } from '../components/unified/FormLayout';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';

type GameFormValue = {
  name: string;
};

const emptyForm: GameFormValue = { name: '' };

type GamesPageProps = {
  initialCreate?: boolean;
  initialEditId?: string;
};

export const GamesPage: React.FC<GamesPageProps> = ({ initialCreate, initialEditId }) => {
  const [games, setGames] = useState<GameDto[]>([]);
  const [creating, setCreating] = useState(Boolean(initialCreate));
  const [form, setForm] = useState<GameFormValue>(emptyForm);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [filterName, setFilterName] = useState('');
  const [tableMessage, setTableMessage] = useState<string | undefined>(undefined);
  const [tableError, setTableError] = useState<string | undefined>(undefined);

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    listGames()
      .then((data: GameDto[]) => {
        if (!mounted) return;
        setGames(data);
        setTableError(undefined);
      })
      .catch((err: any) => { if (!mounted) return; setGames([]); setTableError(err?.message ?? 'Failed to load games'); })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    if (!initialEditId) return;
    const load = async () => {
      setErrors(undefined);
      try {
        const g = await getGame(initialEditId);
        setEditingId(initialEditId);
        setCreating(false);
        setForm({ name: g.name });
        setDirty(false);
      } catch (err: any) {
        setErrors({ form: err?.message ?? 'Failed to load game' });
      }
    };
    void load();
  }, [initialEditId]);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);

  const resetForm = () => {
    setForm(emptyForm);
    setErrors(undefined);
    setDirty(false);
  };

  const displayedGames = useMemo(() => {
    const query = filterName.trim().toLowerCase();
    return games
      .filter((g) => !query || g.name.toLowerCase().includes(query))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [games, filterName]);

  return (
    <section>
      <h2>Games</h2>
      {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
      {tableError && <div className="form-error" role="alert">{tableError}</div>}
      <div className="actions-header">
        <button onClick={() => { if (!confirmNavigate()) return; setCreating(true); setEditingId(undefined); setDirty(false); }}>Create Game</button>
      </div>
      <table className="games-table" aria-label="Games table">
        <thead>
          <tr>
            <th>
              <div>Name</div>
              <input
                aria-label="Filter by name"
                value={filterName}
                onChange={(e) => setFilterName(e.target.value)}
                placeholder="Filter by name"
              />
            </th>
          </tr>
        </thead>
        <tbody>
          {loading && (
            <tr><td>Loading...</td></tr>
          )}
          {!loading && displayedGames.length === 0 && (
            <tr><td>No games found.</td></tr>
          )}
          {!loading && displayedGames.length > 0 && displayedGames.map((g) => (
            <tr key={g.id} className="games-row">
              <td>
                <button type="button" className="link-button" onClick={async () => {
                  if (!confirmNavigate()) return;
                  setErrors(undefined);
                  try {
                    const game = await getGame(g.id);
                    setEditingId(g.id);
                    setForm({ name: game.name });
                    setDirty(false);
                  } catch (err: any) {
                    setErrors({ form: err?.message ?? 'Failed to load game' });
                  }
                }}>
                  {g.name}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {creating && (
        <form
          className="edit-form"
          aria-label="Create game form"
          onSubmit={async (e) => {
            e.preventDefault();
            const nextErrors: Record<string, string> = {};
            if (!form.name.trim()) nextErrors.name = 'Name is required';
            if (Object.keys(nextErrors).length) { setErrors(nextErrors); return; }
            setSubmitting(true);
            try {
              await createGame({ name: form.name.trim() });
              setCreating(false);
              resetForm();
              const data = await listGames();
              setGames(data);
              setTableMessage('Game created successfully.');
            } catch (err: any) {
              setErrors({ form: err?.message ?? 'Failed to create game' });
            } finally {
              setSubmitting(false);
            }
          }}
        >
          <FormSection title="Basics" description="Primary details for the game profile." id="game-basics">
            <div className="field">
              <label htmlFor="game-name">Name *</label>
              <input
                id="game-name"
                value={form.name}
                onChange={(e) => { setForm({ ...form, name: e.target.value }); setErrors(undefined); setDirty(true); }}
                aria-invalid={Boolean(errors?.name)}
                aria-describedby={errors?.name ? 'game-name-error' : undefined}
                disabled={submitting || loading}
              />
              {errors?.name && <div id="game-name-error" className="field-error" role="alert">{errors.name}</div>}
            </div>
          </FormSection>

          <FormActions submitting={submitting} onCancel={() => { if (!confirmNavigate()) return; setCreating(false); resetForm(); }}>
            {loading && <span className="form-hint">Loading…</span>}
          </FormActions>
          <FormError message={errors?.form} />
        </form>
      )}
      {editingId && (
        <section>
          <h3>Edit Game</h3>
          <form
            className="edit-form"
            aria-label="Edit game form"
            onSubmit={async (e) => {
              e.preventDefault();
              if (!editingId) return;
              const nextErrors: Record<string, string> = {};
              if (!form.name.trim()) nextErrors.name = 'Name is required';
              if (Object.keys(nextErrors).length) { setErrors(nextErrors); return; }
              setSubmitting(true);
              try {
                await updateGame(editingId, { name: form.name.trim() });
                const data = await listGames();
                setGames(data);
                setTableMessage('Game updated successfully.');
                setEditingId(undefined);
                resetForm();
                setDirty(false);
              } catch (err: any) {
                setErrors({ form: err?.message ?? 'Failed to update game' });
              } finally {
                setSubmitting(false);
              }
            }}
          >
            <FormSection title="Basics" description="Primary details for the game profile." id="game-edit-basics">
              <div className="field">
                <label htmlFor="game-edit-name">Name *</label>
                <input
                  id="game-edit-name"
                  value={form.name}
                  onChange={(e) => { setForm({ ...form, name: e.target.value }); setErrors(undefined); setDirty(true); }}
                  aria-invalid={Boolean(errors?.name)}
                  aria-describedby={errors?.name ? 'game-edit-name-error' : undefined}
                  disabled={submitting || loading}
                />
                {errors?.name && <div id="game-edit-name-error" className="field-error" role="alert">{errors.name}</div>}
              </div>
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
            await deleteGame(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            const data = await listGames();
            setGames(data);
            setTableMessage('Game deleted successfully.');
            resetForm();
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: game is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setErrors({ form: err?.message ?? 'Failed to delete game' });
            }
          }
        }}
      />
    </section>
  );
};
