import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
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
  const [items, setItems] = useState<ListItem[]>([]);
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

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    listGames()
      .then((data: GameDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((g) => ({
          id: g.id,
          name: g.name
        }));
        setItems(mapped);
      })
      .catch(() => setItems([]))
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

  return (
    <section>
      <h2>Games</h2>
      <div className="actions-header">
        <button onClick={() => { if (!confirmNavigate()) return; setCreating(true); setEditingId(undefined); setDirty(false); }}>Create Game</button>
      </div>
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
              const mapped: ListItem[] = data.map((g) => ({
                id: g.id,
                name: g.name
              }));
              setItems(mapped);
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
      <List
        items={items}
        emptyMessage="No games found."
        onSelect={async (id) => {
          if (!confirmNavigate()) return;
          setErrors(undefined);
          try {
            const g = await getGame(id);
            setEditingId(id);
            setForm({ name: g.name });
            setDirty(false);
          } catch (err: any) {
            setErrors({ form: err?.message ?? 'Failed to load game' });
          }
        }}
      />
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
                const mapped: ListItem[] = data.map((g) => ({
                  id: g.id,
                  name: g.name
                }));
                setItems(mapped);
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
            const mapped: ListItem[] = data.map((g) => ({
              id: g.id,
              name: g.name
            }));
            setItems(mapped);
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
