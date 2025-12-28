import React, { useEffect, useMemo, useState } from 'react';
import { List, ListItem } from '../components/List';
import { listCommands, CommandDto, CommandStepDto, createCommand, getCommand, updateCommand, deleteCommand } from '../services/commands';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listActions, ActionDto } from '../services/actions';
import { CommandForm, CommandFormValue, ParameterEntry, StepEntry, DetectionTargetForm } from '../components/commands/CommandForm';
import { SearchableOption } from '../components/SearchableDropdown';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';
import { navigateToUnified } from '../lib/navigation';

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const paramsFromDto = (obj?: Record<string, unknown>): ParameterEntry[] => {
  if (!obj) return [];
  return Object.entries(obj).map(([key, val]) => ({ id: makeId(), key, value: String(val ?? '') }));
};

const paramsToObject = (entries: ParameterEntry[]): Record<string, unknown> | undefined => {
  const result: Record<string, unknown> = {};
  for (const p of entries) {
    if (!p.key.trim()) continue;
    result[p.key.trim()] = p.value;
  }
  return Object.keys(result).length ? result : undefined;
};

const emptyForm: CommandFormValue = { name: '', parameters: [], steps: [], detection: undefined };

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

const detectionFromDto = (dto?: { referenceImageId: string; confidence?: number; offsetX?: number; offsetY?: number }): DetectionTargetForm | undefined => {
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
  if (!hasValue) return undefined;
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

export const CommandsPage: React.FC<CommandsPageProps> = ({ initialCreate, initialEditId }) => {
  const [items, setItems] = useState<ListItem[]>([]);
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

  useEffect(() => {
    let mounted = true;
    setLoadingCommands(true);
    Promise.all([listCommands(), listActions()])
      .then(([cmds, acts]: [CommandDto[], ActionDto[]]) => {
        if (!mounted) return;
        const mapped: ListItem[] = cmds.map((c) => ({
          id: c.id,
          name: c.name,
          details: { steps: c.steps?.length ?? c.actions?.length ?? 0 }
        }));
        setItems(mapped);
        setActionOptions(acts.map((a) => ({ value: a.id, label: a.name, description: a.description })));
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
      })
      .catch(() => {
        if (!mounted) return;
        setItems([]);
        setActionOptions([]);
        setCommandOptions([]);
      })
      .finally(() => {
        if (mounted) setLoadingCommands(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  const filteredCommandOptions = useMemo(() => {
    if (!editingId) return commandOptions;
    return commandOptions.filter((c) => c.value !== editingId);
  }, [commandOptions, editingId]);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);

  const reloadCommands = async () => {
    const data = await listCommands();
    const mapped: ListItem[] = data.map((c) => ({
      id: c.id,
      name: c.name,
      details: { steps: c.steps?.length ?? c.actions?.length ?? 0 }
    }));
    setItems(mapped);
    setCommandOptions(data.map((c) => ({ value: c.id, label: c.name })));
  };

  useEffect(() => {
    if (!initialEditId) return;
    const load = async () => {
      setErrors(undefined);
      try {
        const c = await getCommand(initialEditId);
        setEditingId(initialEditId);
        setCreating(false);
        setForm({
          name: c.name,
          parameters: paramsFromDto(c.parameters),
          steps: stepsFromDto(c),
          detection: detectionFromDto(c.detectionTarget)
        });
        setDirty(false);
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

  return (
    <section>
      <h2>Commands</h2>
      <div className="actions-header">
        <button onClick={() => { if (!confirmNavigate()) return; setCreating(true); setEditingId(undefined); setForm(emptyForm); setErrors(undefined); setDirty(false); }}>Create Command</button>
      </div>
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
                parameters: paramsToObject(form.parameters),
                steps: stepsToDto(form.steps),
                detectionTarget: detectionResult && 'value' in detectionResult ? detectionResult.value : undefined,
              });
              setCreating(false);
              setForm(emptyForm);
              setDirty(false);
              await reloadCommands();
            } catch (err: any) {
              setErrors({ form: err?.message ?? 'Failed to create command' });
            } finally {
              setSubmitting(false);
            }
          }}
        />
      )}
      <List
        items={items}
        emptyMessage="No commands found."
        onSelect={async (id) => {
          if (!confirmNavigate()) return;
          setErrors(undefined);
          try {
            const c = await getCommand(id);
            setEditingId(id);
            setCreating(false);
            setForm({
              name: c.name,
              parameters: paramsFromDto(c.parameters),
              steps: stepsFromDto(c),
              detection: detectionFromDto(c.detectionTarget)
            });
            setDirty(false);
          } catch (err: any) {
            setErrors({ form: err?.message ?? 'Failed to load command' });
          }
        }}
      />
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
                  parameters: paramsToObject(form.parameters),
                  steps: stepsToDto(form.steps),
                  detectionTarget: detectionResult && 'value' in detectionResult ? detectionResult.value : undefined,
                });
                await reloadCommands();
                setDirty(false);
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
