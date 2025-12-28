import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listGames, GameDto, createGame, GameCreate, getGame, updateGame, deleteGame } from '../services/games';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { FormError } from '../components/Form';
import { FormActions, FormSection } from '../components/unified/FormLayout';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';

type MetadataEntry = { id: string; key: string; value: string };

type GameFormValue = {
  name: string;
  metadata: MetadataEntry[];
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: GameFormValue = { name: '', metadata: [] };

const metadataFromDto = (meta?: Record<string, unknown>): MetadataEntry[] => {
  if (!meta) return [];
  return Object.entries(meta).map(([key, value]) => ({ id: makeId(), key, value: String(value ?? '') }));
};

const metadataToDto = (entries: MetadataEntry[]): Record<string, unknown> | undefined => {
  const result: Record<string, unknown> = {};
  for (const e of entries) {
    const k = e.key.trim();
    if (!k) continue;
    result[k] = e.value;
  }
  return Object.keys(result).length ? result : undefined;
};

export const GamesPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
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
          name: g.name,
          details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
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
              await createGame({ name: form.name.trim(), metadata: metadataToDto(form.metadata) });
              setCreating(false);
                resetForm();
              const data = await listGames();
              const mapped: ListItem[] = data.map((g) => ({
                id: g.id,
                name: g.name,
                details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
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

          <FormSection title="Metadata" description="Optional metadata stored with the game profile." id="game-metadata">
            <button type="button" onClick={() => { setForm({ ...form, metadata: [...form.metadata, { id: makeId(), key: '', value: '' }] }); setDirty(true); }} disabled={submitting}>Add metadata</button>
            {form.metadata.length === 0 && <div className="empty-state">No metadata yet.</div>}
            {form.metadata.map((m, idx) => (
              <div className="field grid-2" key={m.id}>
                <div>
                  <label htmlFor={`meta-key-${m.id}`}>Key</label>
                  <input
                    id={`meta-key-${m.id}`}
                    value={m.key}
                    onChange={(e) => {
                      const next = [...form.metadata];
                      next[idx] = { ...next[idx], key: e.target.value };
                      setForm({ ...form, metadata: next });
                      setDirty(true);
                    }}
                    disabled={submitting}
                  />
                </div>
                <div>
                  <label htmlFor={`meta-val-${m.id}`}>Value</label>
                  <input
                    id={`meta-val-${m.id}`}
                    value={m.value}
                    onChange={(e) => {
                      const next = [...form.metadata];
                      next[idx] = { ...next[idx], value: e.target.value };
                      setForm({ ...form, metadata: next });
                      setDirty(true);
                    }}
                    disabled={submitting}
                  />
                </div>
                <div className="field-actions">
                  <button type="button" onClick={() => { setForm({ ...form, metadata: form.metadata.filter((x) => x.id !== m.id) }); setDirty(true); }} disabled={submitting}>Delete</button>
                </div>
              </div>
            ))}
            <div className="form-hint">Use key/value pairs for lightweight game metadata (e.g., mode, platform).</div>
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
            setForm({ name: g.name, metadata: metadataFromDto(g.metadata) });
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
                await updateGame(editingId, { name: form.name.trim(), metadata: metadataToDto(form.metadata) });
                const data = await listGames();
                const mapped: ListItem[] = data.map((g) => ({
                  id: g.id,
                  name: g.name,
                  details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
                }));
                setItems(mapped);
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

            <FormSection title="Metadata" description="Optional metadata stored with the game profile." id="game-edit-metadata">
              <button type="button" onClick={() => { setForm({ ...form, metadata: [...form.metadata, { id: makeId(), key: '', value: '' }] }); setDirty(true); }} disabled={submitting}>Add metadata</button>
              {form.metadata.length === 0 && <div className="empty-state">No metadata yet.</div>}
              {form.metadata.map((m, idx) => (
                <div className="field grid-2" key={m.id}>
                  <div>
                    <label htmlFor={`edit-meta-key-${m.id}`}>Key</label>
                    <input
                      id={`edit-meta-key-${m.id}`}
                      value={m.key}
                      onChange={(e) => {
                        const next = [...form.metadata];
                        next[idx] = { ...next[idx], key: e.target.value };
                        setForm({ ...form, metadata: next });
                        setDirty(true);
                      }}
                      disabled={submitting}
                    />
                  </div>
                  <div>
                    <label htmlFor={`edit-meta-val-${m.id}`}>Value</label>
                    <input
                      id={`edit-meta-val-${m.id}`}
                      value={m.value}
                      onChange={(e) => {
                        const next = [...form.metadata];
                        next[idx] = { ...next[idx], value: e.target.value };
                        setForm({ ...form, metadata: next });
                        setDirty(true);
                      }}
                      disabled={submitting}
                    />
                  </div>
                  <div className="field-actions">
                    <button type="button" onClick={() => { setForm({ ...form, metadata: form.metadata.filter((x) => x.id !== m.id) }); setDirty(true); }} disabled={submitting}>Delete</button>
                  </div>
                </div>
              ))}
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
              name: g.name,
              details: g.metadata ? { metaKeys: Object.keys(g.metadata).length } : undefined
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
