import React, { useEffect, useMemo, useState } from 'react';
import { CommandForm, CommandFormValue, DetectionTargetForm, StepEntry } from '../components/commands/CommandForm';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';
import { navigateToUnified } from '../lib/navigation';
import { ApiError } from '../lib/api';
import { SearchableOption } from '../components/SearchableDropdown';
import { listCommands, getCommand, createCommand, updateCommand, deleteCommand, CommandDto, CommandStepDto } from '../services/commands';
import { listGames, GameDto } from '../services/games';
import { listActions as listLegacyActions, ActionDto as LegacyActionDto } from '../services/actions';
import { listActions as listDomainActions } from '../services/actionsApi';
import './CommandsPage.css';

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: CommandFormValue = { name: '', steps: [], detection: undefined };

const stepsFromDto = (dto: CommandDto): StepEntry[] => {
  if (dto.steps && dto.steps.length > 0) {
    return dto.steps
      .sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
      .map((s) => ({ id: makeId(), type: s.type, targetId: s.targetId }));
  }
  if (dto.actions && dto.actions.length > 0) {
    return dto.actions.map((a) => ({ id: makeId(), type: 'Action' as const, targetId: a }));
  }
  return [];
};

const stepsToDto = (steps: StepEntry[]): CommandStepDto[] => steps.map((s, idx) => ({ type: s.type, targetId: s.targetId, order: idx }));

const detectionFromDto = (dto?: { referenceImageId: string; confidence?: number; offsetX?: number; offsetY?: number; selectionStrategy?: string }): DetectionTargetForm | undefined => {
  if (!dto) return undefined;
  return {
    referenceImageId: dto.referenceImageId,
    confidence: dto.confidence !== undefined ? String(dto.confidence) : undefined,
    offsetX: dto.offsetX !== undefined ? String(dto.offsetX) : undefined,
    offsetY: dto.offsetY !== undefined ? String(dto.offsetY) : undefined,
  };
};

const detectionToDto = (form?: DetectionTargetForm) => {
  if (!form) return undefined;
  const hasValue = form.referenceImageId?.trim() || form.confidence || form.offsetX || form.offsetY;
  if (!hasValue) return { value: null } as const;
  if (!form.referenceImageId.trim()) return { error: 'Reference image ID is required when detection is configured' } as const;
  const confidence = form.confidence && form.confidence !== '' ? Number(form.confidence) : undefined;
  const offsetX = form.offsetX && form.offsetX !== '' ? Number(form.offsetX) : undefined;
  const offsetY = form.offsetY && form.offsetY !== '' ? Number(form.offsetY) : undefined;
  return { value: { referenceImageId: form.referenceImageId.trim(), confidence, offsetX, offsetY } } as const;
};

type CommandsPageProps = {
  initialCreate?: boolean;
  initialEditId?: string;
};

type CommandRow = {
  id: string;
  name: string;
  stepCount: number;
  gameId?: string;
};

export const CommandsPage: React.FC<CommandsPageProps> = ({ initialCreate, initialEditId }) => {
  const [commands, setCommands] = useState<CommandDto[]>([]);
  const [games, setGames] = useState<GameDto[]>([]);
  const [actionGameMap, setActionGameMap] = useState<Map<string, string>>(new Map());
  const [creating, setCreating] = useState(Boolean(initialCreate));
  const [form, setForm] = useState<CommandFormValue>(emptyForm);
  const [actionOptions, setActionOptions] = useState<SearchableOption[]>([]);
  const [commandOptions, setCommandOptions] = useState<SearchableOption[]>([]);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [loadingCommands, setLoadingCommands] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [filterGame, setFilterGame] = useState('');
  const [filterName, setFilterName] = useState('');
  const [tableMessage, setTableMessage] = useState<string | undefined>(undefined);
  const [tableError, setTableError] = useState<string | undefined>(undefined);

  useEffect(() => {
    let mounted = true;
    setLoadingCommands(true);
    const load = async () => {
      try {
        const [cmds, legacyActions, domainActions, gameList] = await Promise.all([
          listCommands(),
          listLegacyActions(),
          listDomainActions(),
          listGames()
        ]);
        if (!mounted) return;
        setCommands(cmds);
        setGames(gameList);
        setActionOptions(legacyActions.map((a: LegacyActionDto) => ({ value: a.id, label: a.name, description: a.description })));
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
        setActionGameMap(new Map(domainActions.map((a) => [a.id, a.gameId ?? ''])));
        setTableError(undefined);
      } catch (err: any) {
        if (!mounted) return;
        setCommands([]);
        setGames([]);
        setActionOptions([]);
        setCommandOptions([]);
        setActionGameMap(new Map());
        setTableError(err?.message ?? 'Failed to load commands');
      } finally {
        if (mounted) setLoadingCommands(false);
      }
    };
    void load();
    return () => { mounted = false; };
  }, []);

  const reloadCommands = async () => {
    const data = await listCommands();
    setCommands(data);
    setCommandOptions(data.map((c) => ({ value: c.id, label: c.name })));
  };

  const loadCommandIntoForm = async (id: string) => {
    const c = await getCommand(id);
    setForm({
      name: c.name,
      steps: stepsFromDto(c),
      detection: detectionFromDto(c.detection)
    });
    setDirty(false);
  };

  const filteredCommandOptions = useMemo(() => {
    if (!editingId) return commandOptions;
    return commandOptions.filter((c) => c.value !== editingId);
  }, [commandOptions, editingId]);

  const gameLookup = useMemo(() => {
    const map = new Map<string, string>();
    games.forEach((g) => map.set(g.id, g.name));
    return map;
  }, [games]);

  const commandRows: CommandRow[] = useMemo(() => {
    return commands.map((c) => {
      const stepCount = c.steps?.length ?? c.actions?.length ?? 0;
      const actionStep = c.steps?.find((s) => s.type === 'Action');
      const gameId = actionStep ? actionGameMap.get(actionStep.targetId) : undefined;
      return { id: c.id, name: c.name, stepCount, gameId };
    });
  }, [commands, actionGameMap]);

  const displayedCommands = useMemo(() => {
    const nameQuery = filterName.trim().toLowerCase();
    return [...commandRows]
      .filter((c) => !filterGame || c.gameId === filterGame)
      .filter((c) => !nameQuery || c.name.toLowerCase().includes(nameQuery))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [commandRows, filterGame, filterName]);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);

  useEffect(() => {
    if (!initialEditId) return;
    const load = async () => {
      setErrors(undefined);
      try {
        setEditingId(initialEditId);
        setCreating(false);
        await loadCommandIntoForm(initialEditId);
      } catch (err: any) {
        setErrors({ form: err?.message ?? 'Failed to load command' });
      }
    };
    void load();
  }, [initialEditId]);

  const validate = (v: CommandFormValue): Record<string, string> | undefined => {
    const next: Record<string, string> = {};
    if (!v.name.trim()) next.name = 'Name is required';
    const detectionResult = detectionToDto(v.detection);
    if (detectionResult && 'error' in detectionResult) next.detection = detectionResult.error;
    return Object.keys(next).length ? next : undefined;
  };

  const tableLoading = loadingCommands;

  return (
    <section className="commands-page">
      <h2>Commands</h2>
      {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
      {tableError && <div className="form-error" role="alert">{tableError}</div>}
      <div className="actions-header">
        <button onClick={() => { if (!confirmNavigate()) return; setCreating(true); setEditingId(undefined); setForm(emptyForm); setErrors(undefined); setDirty(false); }}>Create Command</button>
      </div>

      <table className="commands-table" aria-label="Commands table">
        <thead>
          <tr>
            <th>
              <div>Game</div>
              <select value={filterGame} onChange={(e) => setFilterGame(e.target.value)} disabled={tableLoading}>
                <option value="">All games</option>
                {games.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
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
              <div>Steps</div>
            </th>
          </tr>
        </thead>
        <tbody>
          {tableLoading && (
            <tr><td colSpan={3}>Loading...</td></tr>
          )}
          {!tableLoading && displayedCommands.length === 0 && (
            <tr><td colSpan={3}>No commands found.</td></tr>
          )}
          {!tableLoading && displayedCommands.length > 0 && displayedCommands.map((c) => (
            <tr key={c.id} className="commands-row">
              <td>{gameLookup.get(c.gameId ?? '') ?? (c.gameId || '—')}</td>
              <td className="command-name-cell">
                <button
                  type="button"
                  className="link-button"
                  title={c.name}
                  onClick={() => { if (!confirmNavigate()) return; setEditingId(c.id); setCreating(false); void loadCommandIntoForm(c.id); }}
                >
                  <span className="command-name">{c.name}</span>
                </button>
              </td>
              <td>{c.stepCount}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {creating && (
        <CommandForm
          value={form}
          actionOptions={actionOptions}
          commandOptions={commandOptions}
          errors={errors}
          submitting={submitting}
          loading={loadingCommands}
          onCreateNewAction={() => navigateToUnified('Actions', { create: true, newTab: true })}
          onChange={(v) => { setErrors(undefined); setForm(v); setDirty(true); }}
          onCancel={() => { if (!confirmNavigate()) return; setCreating(false); setForm(emptyForm); setErrors(undefined); setDirty(false); }}
          onSubmit={async () => {
            const validation = validate(form);
            if (validation) {
              setErrors(validation);
              return;
            }
            setSubmitting(true);
            try {
              const detectionResult = detectionToDto(form.detection);
              if (detectionResult && 'error' in detectionResult) {
                setErrors({ detection: detectionResult.error });
                return;
              }
              await createCommand({
                name: form.name.trim(),
                steps: stepsToDto(form.steps),
                detection: detectionResult?.value,
              });
              setCreating(false);
              setForm(emptyForm);
              setDirty(false);
              setTableMessage('Command created successfully.');
              await reloadCommands();
            } catch (err: any) {
              setErrors({ form: err?.message ?? 'Failed to create command' });
            } finally {
              setSubmitting(false);
            }
          }}
        />
      )}

      {editingId && (
        <section>
          <h3>Edit Command</h3>
          <CommandForm
            value={form}
            actionOptions={actionOptions}
            commandOptions={filteredCommandOptions}
            errors={errors}
            submitting={submitting}
            loading={loadingCommands}
            onCreateNewAction={() => navigateToUnified('Actions', { create: true, newTab: true })}
            onChange={(v) => { setErrors(undefined); setForm(v); setDirty(true); }}
            onCancel={() => { if (!confirmNavigate()) return; setEditingId(undefined); setForm(emptyForm); setErrors(undefined); setDirty(false); }}
            onSubmit={async () => {
              if (!editingId) return;
              const validation = validate(form);
              if (validation) {
                setErrors(validation);
                return;
              }
              setSubmitting(true);
              try {
                const detectionResult = detectionToDto(form.detection);
                if (detectionResult && 'error' in detectionResult) {
                  setErrors({ detection: detectionResult.error });
                  return;
                }
                await updateCommand(editingId, {
                  name: form.name.trim(),
                  steps: stepsToDto(form.steps),
                  detection: detectionResult?.value,
                });
                await reloadCommands();
                setEditingId(undefined);
                setForm(emptyForm);
                setDirty(false);
                setTableMessage('Command updated successfully.');
              } catch (err: any) {
                setErrors({ form: err?.message ?? 'Failed to update command' });
              } finally {
                setSubmitting(false);
              }
            }}
          />
          <div className="form-actions">
            <button type="button" className="btn btn-danger" onClick={() => setDeleteOpen(true)}>Delete</button>
          </div>
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
            await deleteCommand(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            setForm(emptyForm);
            setDirty(false);
            setTableMessage('Command deleted successfully.');
            await reloadCommands();
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: command is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setErrors({ form: err?.message ?? 'Failed to delete command' });
            }
          }
        }}
      />
    </section>
  );
};
