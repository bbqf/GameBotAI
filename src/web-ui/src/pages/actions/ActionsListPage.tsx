import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { ConfirmDeleteModal } from '../../components/ConfirmDeleteModal';
import { ActionForm, ActionFormValue } from '../../components/actions/ActionForm';
import { CreateActionPage } from './CreateActionPage';
import { useActionTypes } from '../../services/useActionTypes';
import { useGames } from '../../services/useGames';
import { deleteAction, duplicateAction, getAction, listActions, updateAction } from '../../services/actionsApi';
import { coerceValue, validateAttributes } from '../../services/validation';
import { ApiError } from '../../lib/api';
import { ActionDto, ActionType, ValidationMessage } from '../../types/actions';

type Mode = 'list' | 'create' | 'edit';

type ActionsListPageProps = {
  initialMode?: Mode;
  initialEditId?: string;
};

export const ActionsListPage: React.FC<ActionsListPageProps> = ({ initialMode = 'list', initialEditId }) => {
  const { data: typeCatalog, loading: typesLoading, error: typesError } = useActionTypes();
  const { data: gamesCatalog, loading: gamesLoading, error: gamesError } = useGames();
  const actionTypes = useMemo(() => typeCatalog?.items ?? [], [typeCatalog]);
  const games = useMemo(() => gamesCatalog ?? [], [gamesCatalog]);

  const [mode, setMode] = useState<Mode>(initialMode);
  const [filterType, setFilterType] = useState('');
  const [filterGame, setFilterGame] = useState('');
  const [filterName, setFilterName] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | undefined>(undefined);
  const [message, setMessage] = useState<string | undefined>(undefined);
  const [actions, setActions] = useState<ActionDto[]>([]);
  const [prefill, setPrefill] = useState<ActionDto | undefined>(undefined);

  const [editId, setEditId] = useState<string | undefined>(undefined);
  const [editForm, setEditForm] = useState<ActionFormValue>({ name: '', gameId: '', type: '', attributes: {} });
  const [editErrors, setEditErrors] = useState<ValidationMessage[]>([]);
  const [editLoading, setEditLoading] = useState(false);
  const [editMessage, setEditMessage] = useState<string | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);

  const filterOptions = useMemo(() => [
    { value: '', label: 'All types' },
    ...actionTypes.map((t: ActionType) => ({ value: t.key, label: t.displayName }))
  ], [actionTypes]);

  const gameFilterOptions = useMemo(() => [
    { value: '', label: 'All games' },
    ...games.map((g) => ({ value: g.id, label: g.name }))
  ], [games]);

  const loadActions = async (type?: string, gameId?: string) => {
    setLoading(true);
    setError(undefined);
    try {
      const data = await listActions({ type, gameId });
      setActions(data);
    } catch (err: any) {
      setError(err?.message ?? 'Failed to load actions');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadActions(filterType || undefined, filterGame || undefined);
  }, [filterType, filterGame]);

  const selectAction = useCallback(async (id: string) => {
    setMode('edit');
    setEditId(id);
    setEditLoading(true);
    setEditErrors([]);
    setEditMessage(undefined);
    try {
      const a = await getAction(id);
      const normalizedAttrs: Record<string, unknown> = (() => {
        const t = actionTypes.find((type) => type.key === a.type);
        if (!t) return a.attributes ?? {};
        const next: Record<string, unknown> = {};
        for (const def of t.attributeDefinitions) {
          const { value } = coerceValue(def.dataType, a.attributes?.[def.key] as any);
          if (value !== undefined) next[def.key] = value;
        }
        return next;
      })();
      setEditForm({ name: a.name, gameId: a.gameId ?? '', type: a.type, attributes: normalizedAttrs });
    } catch (err: any) {
      setError(err?.message ?? 'Failed to load action');
      setMode('list');
      setEditId(undefined);
    } finally {
      setEditLoading(false);
    }
  }, [actionTypes]);

  useEffect(() => {
    if (initialEditId) {
      void selectAction(initialEditId);
      return;
    }
    if (initialMode === 'create') {
      setMode('create');
    }
  }, [initialEditId, initialMode, selectAction]);

  const runValidation = (): ValidationMessage[] => {
    const messages: ValidationMessage[] = [];
    if (!editForm.name.trim()) messages.push({ field: 'name', message: 'Name is required', severity: 'error' });
    if (!editForm.gameId) messages.push({ field: 'gameId', message: 'Game is required', severity: 'error' });
    if (!editForm.type) messages.push({ field: 'type', message: 'Action type is required', severity: 'error' });
    const selectedType = actionTypes.find((t) => t.key === editForm.type);
    if (selectedType) messages.push(...validateAttributes(selectedType.attributeDefinitions, editForm.attributes));
    return messages;
  };

  const handleUpdate = async () => {
    if (!editId) return;
    setEditMessage(undefined);
    const validation = runValidation();
    if (validation.length > 0) {
      setEditErrors(validation);
      return;
    }
    try {
      const attributes = (editForm.type === 'connect-to-game' && editForm.gameId)
        ? { ...editForm.attributes, gameId: editForm.gameId }
        : editForm.attributes;

      await updateAction(editId, { name: editForm.name.trim(), gameId: editForm.gameId, type: editForm.type, attributes });
      setEditErrors([]);
      setEditMessage(undefined);
      setMessage('Action updated successfully.');
      setMode('list');
      setEditId(undefined);
      await loadActions(filterType || undefined, filterGame || undefined);
    } catch (err: any) {
      if (err instanceof ApiError && err.errors?.length) {
        const mapped: ValidationMessage[] = err.errors.map((e) => ({ field: e.field, message: e.message, severity: 'error' }));
        setEditErrors(mapped);
      } else {
        setEditErrors([{ message: err?.message ?? 'Failed to update action', severity: 'error' }]);
      }
    }
  };

  const handleDuplicate = async () => {
    if (!editId) return;
    setMessage(undefined);
    try {
      await duplicateAction(editId);
      setMessage('Action duplicated successfully.');
      await loadActions(filterType || undefined, filterGame || undefined);
    } catch (err: any) {
      setError(err?.message ?? 'Failed to duplicate action');
    }
  };

  const handleCopyToCreate = () => {
    if (!editId) return;
    setPrefill({ id: editId, name: editForm.name, gameId: editForm.gameId, type: editForm.type, attributes: editForm.attributes });
    setMode('create');
  };

  const handleCreateComplete = async () => {
    setPrefill(undefined);
    setMode('list');
    setMessage('Action created successfully.');
    await loadActions(filterType || undefined, filterGame || undefined);
  };

  const handleDelete = async () => {
    if (!editId) return;
    try {
      await deleteAction(editId);
      setDeleteMessage(undefined);
      setDeleteReferences(undefined);
      setDeleteOpen(false);
      setMode('list');
      setEditId(undefined);
      await loadActions(filterType || undefined, filterGame || undefined);
      setMessage('Action deleted successfully.');
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        setDeleteMessage(err.message || 'Cannot delete: action is referenced.');
        setDeleteReferences(err.references || undefined);
        setDeleteOpen(true);
      } else {
        setDeleteOpen(false);
        setError(err?.message ?? 'Failed to delete action');
      }
    }
  };

  const gameLookup = useMemo(() => {
    const map = new Map<string, string>();
    games.forEach((g) => map.set(g.id, g.name));
    return map;
  }, [games]);

  const typeLookup = useMemo(() => {
    const map = new Map<string, string>();
    actionTypes.forEach((t) => map.set(t.key, t.displayName));
    return map;
  }, [actionTypes]);

  const displayedActions = useMemo(() => {
    const nameQuery = filterName.trim().toLowerCase();
    return [...actions]
      .filter((a) => !filterGame || a.gameId === filterGame)
      .filter((a) => !filterType || a.type === filterType)
      .filter((a) => !nameQuery || a.name.toLowerCase().includes(nameQuery))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [actions, filterGame, filterName, filterType]);

  const tableLoading = loading || gamesLoading || typesLoading;

  if (mode === 'create') {
    return (
      <CreateActionPage
        initialValue={prefill ? { name: `${prefill.name} copy`, gameId: prefill.gameId ?? '', type: prefill.type, attributes: prefill.attributes ?? {} } : undefined}
        onCancel={() => { setMode('list'); setPrefill(undefined); }}
        onCreated={() => { void handleCreateComplete(); }}
      />
    );
  }

  return (
    <section>
      <h2>Actions</h2>
      {typesError && <div className="form-error" role="alert">{typesError}</div>}
      {gamesError && <div className="form-error" role="alert">{gamesError}</div>}
      {message && <div className="form-hint">{message}</div>}
      <div className="actions-header">
        <button type="button" onClick={() => { setPrefill(undefined); setMode('create'); }}>Create Action</button>
      </div>
      {error && <div className="form-error" role="alert">{error}</div>}
      <table className="actions-table" aria-label="Actions table">
        <thead>
          <tr>
            <th>
              <div>Game</div>
              <select value={filterGame} onChange={(e) => setFilterGame(e.target.value)} disabled={gamesLoading}>
                {gameFilterOptions.map((o) => (
                  <option key={o.value} value={o.value}>{o.label}</option>
                ))}
              </select>
            </th>
            <th>
              <div>Name</div>
              <input
                aria-label="Filter by name"
                value={filterName}
                onChange={(e) => setFilterName(e.target.value)}
                placeholder="Filter by name"
              />
            </th>
            <th>
              <div>Type</div>
              <select value={filterType} onChange={(e) => setFilterType(e.target.value)} disabled={typesLoading}>
                {filterOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </th>
          </tr>
        </thead>
        <tbody>
          {tableLoading && (
            <tr><td colSpan={3}>Loading...</td></tr>
          )}
          {!tableLoading && displayedActions.length === 0 && (
            <tr><td colSpan={3}>No actions found.</td></tr>
          )}
          {!tableLoading && displayedActions.length > 0 && displayedActions.map((a) => (
            <tr key={a.id} className="actions-row">
              <td>{gameLookup.get(a.gameId) ?? (a.gameId || '—')}</td>
              <td>
                <button type="button" className="link-button" onClick={() => { void selectAction(a.id); }}>
                  {a.name}
                </button>
              </td>
              <td>{typeLookup.get(a.type) ?? (a.type || '—')}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {mode === 'edit' && editId && (
        <div className="edit-form">
          <h3>Edit Action</h3>
          {editMessage && <div className="form-hint">{editMessage}</div>}
          <ActionForm
            actionTypes={actionTypes}
            games={games}
            value={editForm}
            loading={typesLoading || gamesLoading || editLoading}
            errors={editErrors}
            submitting={false}
            onChange={(v) => { setEditErrors([]); setEditForm(v); }}
            onSubmit={() => { void handleUpdate(); }}
            onCancel={() => { setMode('list'); setEditId(undefined); setEditErrors([]); setEditMessage(undefined); }}
            extraActions={(
              <>
                <button type="button" onClick={() => { setDeleteOpen(true); }}>Delete</button>
                <button type="button" onClick={() => { void handleDuplicate(); }}>Duplicate</button>
                <button type="button" onClick={handleCopyToCreate}>Create from copy</button>
              </>
            )}
          />
        </div>
      )}

      <ConfirmDeleteModal
        open={deleteOpen}
        itemName={editForm.name}
        message={deleteMessage}
        references={deleteReferences}
        onCancel={() => setDeleteOpen(false)}
        onConfirm={() => { void handleDelete(); }}
      />
    </section>
  );
};
