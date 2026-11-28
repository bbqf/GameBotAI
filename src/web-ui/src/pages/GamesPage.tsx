import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { listGames, GameDto, createGame, getGame, updateGame, deleteGame } from '../services/games';
import { FormError } from '../components/Form';
import { FormSection } from '../components/unified/FormLayout';
import { Button, ConfirmDialog, FooterBar, ListView, PageHeader, type ListColumn, type ListState } from '../components/ui';
import { useDeleteFlow } from '../components/authoring/useDeleteFlow';
import { DeleteReferences } from '../components/authoring/DeleteReferences';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';
import { useResetSignal } from '../hooks/useResetSignal';

type GameFormValue = {
  name: string;
  packageName: string;
};

const emptyForm: GameFormValue = { name: '', packageName: '' };

const gameColumns: ListColumn<GameDto>[] = [
  { id: 'name', header: 'Name', width: 'flex', render: (g) => g.name, text: (g) => g.name },
  {
    id: 'packageName',
    header: 'Package name',
    width: 320,
    render: (g) => (g.packageName ? <span className="ui-mono">{g.packageName}</span> : '—'),
    text: (g) => g.packageName ?? '',
  },
];

type GamesPageProps = {
  initialCreate?: boolean;
  initialEditId?: string;
  navResetSignal?: number;
};

export const GamesPage: React.FC<GamesPageProps> = ({ initialCreate, initialEditId, navResetSignal }) => {
  const [games, setGames] = useState<GameDto[]>([]);
  const [creating, setCreating] = useState(Boolean(initialCreate));
  const [form, setForm] = useState<GameFormValue>(emptyForm);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [loadedName, setLoadedName] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [filterName, setFilterName] = useState('');
  const [tableMessage, setTableMessage] = useState<string | undefined>(undefined);
  const [tableError, setTableError] = useState<string | undefined>(undefined);

  const reload = useCallback(() => {
    setLoading(true);
    return listGames()
      .then((data: GameDto[]) => {
        setGames(data);
        setTableError(undefined);
      })
      .catch((err: any) => { setGames([]); setTableError(err?.message ?? 'Failed to load games'); })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  useEffect(() => {
    if (!initialEditId) return;
    const load = async () => {
      setErrors(undefined);
      try {
        const g = await getGame(initialEditId);
        setEditingId(initialEditId);
        setCreating(false);
        setForm({ name: g.name, packageName: g.packageName ?? '' });
        setLoadedName(g.name);
        setDirty(false);
      } catch (err: any) {
        setErrors({ form: err?.message ?? 'Failed to load game' });
      }
    };
    void load();
  }, [initialEditId]);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);
  const editorOpen = creating || Boolean(editingId);

  const resetForm = () => {
    setForm(emptyForm);
    setLoadedName('');
    setErrors(undefined);
    setDirty(false);
  };

  const backToList = () => {
    if (!confirmNavigate()) return;
    setCreating(false);
    setEditingId(undefined);
    resetForm();
  };

  useResetSignal(navResetSignal, () => { if (editorOpen) backToList(); });

  const del = useDeleteFlow({
    kind: 'game',
    remove: deleteGame,
    onDeleted: (target) => {
      if (editingId === target.id) {
        setEditingId(undefined);
        resetForm();
      }
      void reload();
      setTableMessage('Game deleted.');
    },
    onError: (message) => {
      if (editorOpen) setErrors({ form: message });
      else setTableError(message);
    },
  });

  const displayedGames = useMemo(() => {
    const query = filterName.trim().toLowerCase();
    return games
      .filter((g) => !query || g.name.toLowerCase().includes(query))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [games, filterName]);

  const openCreate = () => {
    if (!confirmNavigate()) return;
    setCreating(true);
    setEditingId(undefined);
    setDirty(false);
  };

  const openEdit = async (id: string) => {
    if (!confirmNavigate()) return;
    setErrors(undefined);
    try {
      const game = await getGame(id);
      setEditingId(id);
      setForm({ name: game.name, packageName: game.packageName ?? '' });
      setLoadedName(game.name);
      setDirty(false);
    } catch (err: any) {
      setErrors({ form: err?.message ?? 'Failed to load game' });
    }
  };

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    const nextErrors: Record<string, string> = {};
    if (!form.name.trim()) nextErrors.name = 'Name is required';
    if (Object.keys(nextErrors).length) { setErrors(nextErrors); return; }
    const payload = { name: form.name.trim(), packageName: form.packageName.trim() || undefined };
    setSubmitting(true);
    try {
      if (editingId) {
        await updateGame(editingId, payload);
        setEditingId(undefined);
      } else {
        await createGame(payload);
        setCreating(false);
      }
      const wasEdit = Boolean(editingId);
      resetForm();
      setGames(await listGames());
      setTableMessage(wasEdit ? 'Game updated.' : 'Game created.');
    } catch (err: any) {
      setErrors({ form: err?.message ?? (editingId ? 'Failed to update game' : 'Failed to create game') });
    } finally {
      setSubmitting(false);
    }
  };

  const listState: ListState = loading && games.length === 0
    ? { kind: 'loading' }
    : tableError && games.length === 0
      ? { kind: 'error', text: tableError, onRetry: () => void reload() }
      : displayedGames.length === 0
        ? { kind: 'empty', text: 'No games.' }
        : 'ready';

  const idPrefix = editingId ? 'game-edit' : 'game';

  if (!editorOpen) {
    return (
      <section>
        <PageHeader title="Games" actions={<Button variant="primary" icon="add" onClick={openCreate}>Create game</Button>} />
        <div className="ui-message-area">
          {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
          {tableError && games.length > 0 && <div className="form-error" role="alert">{tableError}</div>}
          {errors?.form && <div className="form-error" role="alert">{errors.form}</div>}
        </div>
        <div className="ui-filter-bar">
          <input
            aria-label="Filter by name"
            value={filterName}
            onChange={(e) => setFilterName(e.target.value)}
            placeholder="Filter by name"
          />
        </div>
        <ListView<GameDto>
          mode="desktop"
          ariaLabel="Games table"
          tableClassName="games-list"
          columns={gameColumns}
          rows={displayedGames}
          getRowKey={(g) => g.id}
          rowTestId={(g) => `game-row-${g.id}`}
          onRowClick={(g) => void openEdit(g.id)}
          menuLabel={(g) => `More actions for ${g.name}`}
          rowActions={(g) => ({
            visible: [{ id: 'edit', label: 'Edit', ariaLabel: `Edit ${g.name}`, icon: 'edit', onSelect: () => void openEdit(g.id) }],
            menu: [{ id: 'delete', label: 'Delete', ariaLabel: `Delete ${g.name}`, icon: 'delete', tone: 'danger', onSelect: () => del.open({ id: g.id, name: g.name }) }],
          })}
          state={listState}
        />
        {renderDialog()}
      </section>
    );
  }

  function renderDialog() {
    return (
      <ConfirmDialog
        open={Boolean(del.target)}
        title={`Delete game ${del.target?.name ?? ''}?`}
        message="You cannot undo this."
        details={del.conflict && <DeleteReferences conflict={del.conflict} />}
        confirmText="Delete"
        danger
        busy={del.busy}
        confirmDisabled={Boolean(del.conflict)}
        onConfirm={() => void del.confirm()}
        onCancel={del.cancel}
      />
    );
  }

  return (
    <section>
      <PageHeader onBack={backToList} title={creating ? 'New game' : loadedName} />
      <div className="ui-form">
        <form className="edit-form" aria-label={creating ? 'Create game form' : 'Edit game form'} onSubmit={save}>
          <FormSection title="Basics" description="Primary details for the game profile." id={`${idPrefix}-basics`}>
            <div className="field">
              <label htmlFor={`${idPrefix}-name`}>Name *</label>
              <input
                id={`${idPrefix}-name`}
                value={form.name}
                onChange={(e) => { setForm({ ...form, name: e.target.value }); setErrors(undefined); setDirty(true); }}
                aria-invalid={Boolean(errors?.name)}
                aria-describedby={errors?.name ? `${idPrefix}-name-error` : undefined}
                disabled={submitting || loading}
              />
              {errors?.name && <div id={`${idPrefix}-name-error`} className="field-error" role="alert">{errors.name}</div>}
            </div>
            <div className="field">
              <label htmlFor={`${idPrefix}-package-name`}>Package Name</label>
              <input
                id={`${idPrefix}-package-name`}
                value={form.packageName}
                onChange={(e) => { setForm({ ...form, packageName: e.target.value }); setErrors(undefined); setDirty(true); }}
                placeholder="e.g. com.example.game"
                disabled={submitting || loading}
              />
            </div>
          </FormSection>
          <FormError message={errors?.form} />
          <FooterBar
            left={(
              <>
                {editingId && (
                  <Button variant="danger-outline" disabled={submitting} onClick={() => del.open({ id: editingId, name: loadedName })}>Delete</Button>
                )}
                {loading && <span className="form-hint">Loading…</span>}
              </>
            )}
            right={(
              <>
                <Button variant="secondary" onClick={backToList}>Cancel</Button>
                <Button type="submit" variant="primary" disabled={submitting}>Save</Button>
              </>
            )}
          />
        </form>
      </div>
      {renderDialog()}
    </section>
  );
};
