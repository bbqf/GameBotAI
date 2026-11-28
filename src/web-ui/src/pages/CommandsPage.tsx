import React, { useEffect, useMemo, useState } from 'react';
import { CommandForm, CommandFormValue, DetectionTargetForm, StepEntry } from '../components/commands/CommandForm';
import { Button, ConfirmDialog, ListView, PageHeader, type ListColumn, type ListState } from '../components/ui';
import { useDeleteFlow } from '../components/authoring/useDeleteFlow';
import { DeleteReferences } from '../components/authoring/DeleteReferences';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';
import { useResetSignal } from '../hooks/useResetSignal';
import { parseParameterErrors, parameterErrorSummary } from '../lib/validation';
import { mapParameterErrorsToSteps, StepFieldErrors } from './parameterErrorMapping';
import { SearchableOption } from '../components/SearchableDropdown';
import { listCommands, getCommand, createCommand, updateCommand, deleteCommand, CommandDto, CommandStepDto } from '../services/commands';

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: CommandFormValue = { name: '', steps: [], detection: undefined, parameters: [] };

const stepsFromDto = (dto: CommandDto): StepEntry[] => {
  if (dto.steps && dto.steps.length > 0) {
    return dto.steps
      .sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
      .map((s) => ({
        id: makeId(),
        type: s.type,
        targetId: s.targetId,
        primitiveTap: (s.primitiveTap ?? (s.type === 'PrimitiveTap' && dto.detection
          ? { detectionTarget: dto.detection }
          : undefined))
          ? {
            detectionTarget: {
              referenceImageId: (s.primitiveTap?.detectionTarget ?? dto.detection!).referenceImageId,
              confidence: (s.primitiveTap?.detectionTarget ?? dto.detection!).confidence !== undefined ? String((s.primitiveTap?.detectionTarget ?? dto.detection!).confidence) : undefined,
              offsetX: (s.primitiveTap?.detectionTarget ?? dto.detection!).offsetX !== undefined ? String((s.primitiveTap?.detectionTarget ?? dto.detection!).offsetX) : undefined,
              offsetY: (s.primitiveTap?.detectionTarget ?? dto.detection!).offsetY !== undefined ? String((s.primitiveTap?.detectionTarget ?? dto.detection!).offsetY) : undefined,
            },
            holdMs: s.primitiveTap?.holdMs !== undefined && s.primitiveTap.holdMs !== null ? String(s.primitiveTap.holdMs) : undefined,
          }
          : undefined,
        waitForImage: s.waitForImage
          ? {
            detectionTarget: s.waitForImage.detectionTarget
              ? {
                referenceImageId: s.waitForImage.detectionTarget.referenceImageId,
                confidence: s.waitForImage.detectionTarget.confidence !== undefined ? String(s.waitForImage.detectionTarget.confidence) : undefined,
                offsetX: s.waitForImage.detectionTarget.offsetX !== undefined ? String(s.waitForImage.detectionTarget.offsetX) : undefined,
                offsetY: s.waitForImage.detectionTarget.offsetY !== undefined ? String(s.waitForImage.detectionTarget.offsetY) : undefined,
              }
              : undefined,
            timeoutMs: s.waitForImage.timeoutMs !== undefined ? String(s.waitForImage.timeoutMs) : '1000',
          }
          : undefined,
        keyInput: s.keyInput ? { key: s.keyInput.key } : undefined,
        textInput: s.textInput ? { text: s.textInput.text } : undefined,
        swipe: s.swipe
          ? {
            startX: String(s.swipe.startX),
            startY: String(s.swipe.startY),
            endX: String(s.swipe.endX),
            endY: String(s.swipe.endY),
            durationMs: s.swipe.durationMs !== undefined ? String(s.swipe.durationMs) : undefined,
          }
          : undefined,
        ensureGameRunning: s.ensureGameRunning?.forceRestart ? { forceRestart: true } : undefined,
        ensureEmulatorRunning: s.ensureEmulatorRunning
          ? {
            instanceName: s.ensureEmulatorRunning.instanceName,
            instanceIndex: s.ensureEmulatorRunning.instanceIndex !== undefined ? String(s.ensureEmulatorRunning.instanceIndex) : undefined,
            adbSerial: s.ensureEmulatorRunning.adbSerial,
          }
          : undefined,
        // Feature 169: keep the whole map, also keys that the editor has no control for.
        fieldTemplates: s.fieldTemplates && Object.keys(s.fieldTemplates).length > 0
          ? { ...s.fieldTemplates }
          : undefined,
      }));
  }
  return [];
};

// Feature 169: sends the map only when it has one key or more.
const stepsToDto = (steps: StepEntry[]): CommandStepDto[] => steps.map((s, idx) => {
  const dto = stepToDto(s, idx);
  return s.fieldTemplates && Object.keys(s.fieldTemplates).length > 0
    ? { ...dto, fieldTemplates: { ...s.fieldTemplates } }
    : dto;
});

const stepToDto = (s: StepEntry, idx: number): CommandStepDto => {
  if (s.type === 'PrimitiveTap') {
    return {
      type: 'PrimitiveTap',
      order: idx,
      primitiveTap: {
        detectionTarget: {
          referenceImageId: s.primitiveTap?.detectionTarget.referenceImageId?.trim() ?? '',
          confidence: s.primitiveTap?.detectionTarget.confidence !== undefined && s.primitiveTap.detectionTarget.confidence !== ''
            ? Number(s.primitiveTap.detectionTarget.confidence)
            : undefined,
          offsetX: s.primitiveTap?.detectionTarget.offsetX !== undefined && s.primitiveTap.detectionTarget.offsetX !== ''
            ? Number(s.primitiveTap.detectionTarget.offsetX)
            : undefined,
          offsetY: s.primitiveTap?.detectionTarget.offsetY !== undefined && s.primitiveTap.detectionTarget.offsetY !== ''
            ? Number(s.primitiveTap.detectionTarget.offsetY)
            : undefined,
        },
        holdMs: s.primitiveTap?.holdMs !== undefined && s.primitiveTap.holdMs.trim() !== ''
          ? Number(s.primitiveTap.holdMs)
          : undefined,
      }
    };
  }

  if (s.type === 'WaitForImage') {
    const detectionTarget = s.waitForImage?.detectionTarget?.referenceImageId?.trim()
      ? {
        referenceImageId: s.waitForImage.detectionTarget.referenceImageId.trim(),
        confidence: s.waitForImage.detectionTarget.confidence !== undefined && s.waitForImage.detectionTarget.confidence !== ''
          ? Number(s.waitForImage.detectionTarget.confidence)
          : undefined,
        offsetX: s.waitForImage.detectionTarget.offsetX !== undefined && s.waitForImage.detectionTarget.offsetX !== ''
          ? Number(s.waitForImage.detectionTarget.offsetX)
          : undefined,
        offsetY: s.waitForImage.detectionTarget.offsetY !== undefined && s.waitForImage.detectionTarget.offsetY !== ''
          ? Number(s.waitForImage.detectionTarget.offsetY)
          : undefined,
      }
      : undefined;

    return {
      type: 'WaitForImage',
      order: idx,
      waitForImage: {
        detectionTarget,
        timeoutMs: s.waitForImage?.timeoutMs !== undefined && s.waitForImage.timeoutMs !== ''
          ? Number(s.waitForImage.timeoutMs)
          : undefined,
      }
    };
  }

  if (s.type === 'EnsureGameRunning') {
    return s.ensureGameRunning?.forceRestart
      ? { type: 'EnsureGameRunning', order: idx, ensureGameRunning: { forceRestart: true } }
      : { type: 'EnsureGameRunning', order: idx };
  }

  if (s.type === 'GoToHomeScreen') {
    return { type: 'GoToHomeScreen', order: idx };
  }

  if (s.type === 'EnsureEmulatorRunning') {
    return {
      type: 'EnsureEmulatorRunning',
      order: idx,
      ensureEmulatorRunning: {
        instanceName: s.ensureEmulatorRunning?.instanceName?.trim() || undefined,
        instanceIndex: s.ensureEmulatorRunning?.instanceIndex !== undefined && s.ensureEmulatorRunning.instanceIndex !== ''
          ? Number(s.ensureEmulatorRunning.instanceIndex)
          : undefined,
        adbSerial: s.ensureEmulatorRunning?.adbSerial?.trim() ?? '',
      },
    };
  }

  if (s.type === 'KeyInput') {
    return { type: 'KeyInput', order: idx, keyInput: { key: s.keyInput?.key ?? '' } };
  }

  if (s.type === 'TextInput') {
    // Feature 192: the text goes as it is. A space at the start or the end is part of the text.
    return { type: 'TextInput', order: idx, textInput: { text: s.textInput?.text ?? '' } };
  }

  if (s.type === 'Swipe') {
    return {
      type: 'Swipe',
      order: idx,
      swipe: {
        startX: s.swipe?.startX !== undefined && s.swipe.startX !== '' ? Number(s.swipe.startX) : 0,
        startY: s.swipe?.startY !== undefined && s.swipe.startY !== '' ? Number(s.swipe.startY) : 0,
        endX: s.swipe?.endX !== undefined && s.swipe.endX !== '' ? Number(s.swipe.endX) : 0,
        endY: s.swipe?.endY !== undefined && s.swipe.endY !== '' ? Number(s.swipe.endY) : 0,
        durationMs: s.swipe?.durationMs !== undefined && s.swipe.durationMs !== ''
          ? Number(s.swipe.durationMs)
          : undefined,
      },
    };
  }

  return {
    type: s.type,
    targetId: s.targetId ?? '',
    order: idx
  };
};

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
  navResetSignal?: number;
};

type CommandRow = {
  id: string;
  name: string;
  stepCount: number;
};

const commandColumns: ListColumn<CommandRow>[] = [
  { id: 'name', header: 'Name', width: 'flex', render: (c) => c.name, text: (c) => c.name },
  { id: 'steps', header: 'Steps', width: 96, align: 'end', render: (c) => <span className="ui-num">{c.stepCount}</span> },
];

export const CommandsPage: React.FC<CommandsPageProps> = ({ initialCreate, initialEditId, navResetSignal }) => {
  const [commands, setCommands] = useState<CommandDto[]>([]);
  const [creating, setCreating] = useState(Boolean(initialCreate));
  const [form, setForm] = useState<CommandFormValue>(emptyForm);
  const [commandOptions, setCommandOptions] = useState<SearchableOption[]>([]);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [stepFieldErrors, setStepFieldErrors] = useState<StepFieldErrors | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [loadedName, setLoadedName] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [loadingCommands, setLoadingCommands] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [filterName, setFilterName] = useState('');
  const [tableMessage, setTableMessage] = useState<string | undefined>(undefined);
  const [tableError, setTableError] = useState<string | undefined>(undefined);

  const loadAll = async () => {
    try {
      const cmds = await listCommands();
      setCommands(cmds);
      setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
      setTableError(undefined);
    } catch (err: any) {
      setCommands([]);
      setCommandOptions([]);
      setTableError(err?.message ?? 'Failed to load commands');
    } finally {
      setLoadingCommands(false);
    }
  };

  useEffect(() => {
    setLoadingCommands(true);
    void loadAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const reloadCommands = async () => {
    const data = await listCommands();
    setCommands(data);
    setCommandOptions(data.map((c) => ({ value: c.id, label: c.name })));
  };

  const loadCommandIntoForm = async (id: string) => {
    const c = await getCommand(id);
    setLoadedName(c.name);
    setForm({
      name: c.name,
      steps: stepsFromDto(c),
      detection: detectionFromDto(c.detection),
      parameters: c.parameters ?? []
    });
    setDirty(false);
  };

  // Feature 169: the step messages go away with the form messages.
  useEffect(() => {
    if (!errors) setStepFieldErrors(undefined);
  }, [errors]);

  /**
   * Turns a save rejection into the editor's field-error map (feature 078, FR-029). A parameter
   * rejection carries `details` naming the offending field and parameter, so those are surfaced
   * instead of a generic message; anything else falls back to the message as before.
   */
  const toSaveErrors = (err: unknown, fallback: string): Record<string, string> => {
    const parameterErrors = parseParameterErrors(err);
    const summary = parameterErrorSummary(parameterErrors);
    // Feature 169: show each message at the field of the step that the message names. The summary
    // stays, so a message that names no known step still shows.
    setStepFieldErrors(mapParameterErrorsToSteps(parameterErrors, form.steps.map((s) => s.type)));
    if (summary) return { form: summary };
    return { form: (err as { message?: string })?.message ?? fallback };
  };

  const filteredCommandOptions = useMemo(() => {
    if (!editingId) return commandOptions;
    return commandOptions.filter((c) => c.value !== editingId);
  }, [commandOptions, editingId]);

  const commandRows: CommandRow[] = useMemo(() => {
    return commands.map((c) => {
      const stepCount = c.steps?.length ?? 0;
      return { id: c.id, name: c.name, stepCount };
    });
  }, [commands]);

  const displayedCommands = useMemo(() => {
    const nameQuery = filterName.trim().toLowerCase();
    return [...commandRows]
      .filter((c) => !nameQuery || c.name.toLowerCase().includes(nameQuery))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [commandRows, filterName]);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);
  const editorOpen = creating || Boolean(editingId);

  const backToList = () => {
    if (!confirmNavigate()) return;
    setCreating(false);
    setEditingId(undefined);
    setLoadedName('');
    setForm(emptyForm);
    setErrors(undefined);
    setDirty(false);
  };

  useResetSignal(navResetSignal, () => { if (editorOpen) backToList(); });

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
    if (v.steps.length === 0) {
      next.steps = 'Add at least one step before saving (for example, a Tap step).';
    }
    for (const step of v.steps) {
      if (step.type === 'PrimitiveTap') {
        if (!step.primitiveTap?.detectionTarget.referenceImageId?.trim()) {
          next.steps = 'Tap steps require a detection target reference image ID';
          break;
        }
      } else if (step.type === 'WaitForImage') {
        const timeoutValue = step.waitForImage?.timeoutMs?.trim();
        if (!timeoutValue) {
          next.steps = 'Wait for image steps require a timeout in milliseconds';
          break;
        }

        const timeoutMs = Number(timeoutValue);
        if (!Number.isInteger(timeoutMs) || timeoutMs < 0) {
          next.steps = 'Wait for image timeout must be a non-negative integer';
          break;
        }
      } else if (step.type === 'Command' && !step.targetId?.trim()) {
        next.steps = 'Command steps require a target';
        break;
      }
    }
    const detectionResult = detectionToDto(v.detection);
    if (detectionResult && 'error' in detectionResult) next.detection = detectionResult.error ?? 'Invalid detection target';
    return Object.keys(next).length ? next : undefined;
  };

  const reload = () => {
    setLoadingCommands(true);
    return loadAll();
  };

  const del = useDeleteFlow({
    kind: 'command',
    remove: deleteCommand,
    onDeleted: (target) => {
      if (editingId === target.id) {
        setEditingId(undefined);
        setForm(emptyForm);
        setDirty(false);
      }
      setTableMessage('Command deleted.');
      void reloadCommands();
    },
    onError: (message) => {
      if (editorOpen) setErrors({ form: message });
      else setTableError(message);
    },
  });

  const listState: ListState = loadingCommands && commands.length === 0
    ? { kind: 'loading' }
    : tableError && commands.length === 0
      ? { kind: 'error', text: tableError, onRetry: () => void reload() }
      : displayedCommands.length === 0
        ? { kind: 'empty', text: 'No commands.' }
        : 'ready';

  const openCreate = () => {
    if (!confirmNavigate()) return;
    setCreating(true);
    setEditingId(undefined);
    setLoadedName('');
    setForm(emptyForm);
    setErrors(undefined);
    setDirty(false);
  };

  const openEdit = (id: string) => {
    if (!confirmNavigate()) return;
    setEditingId(id);
    setCreating(false);
    void loadCommandIntoForm(id);
  };

  const dialog = (
    <ConfirmDialog
      open={Boolean(del.target)}
      title={`Delete command ${del.target?.name ?? ''}?`}
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

  if (!editorOpen) {
    return (
      <section>
        <PageHeader title="Commands" actions={<Button variant="primary" icon="add" onClick={openCreate}>Create command</Button>} />
        <div className="ui-message-area">
          {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
          {tableError && commands.length > 0 && <div className="form-error" role="alert">{tableError}</div>}
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
        <ListView<CommandRow>
          mode="desktop"
          ariaLabel="Commands table"
          tableClassName="commands-list"
          columns={commandColumns}
          rows={displayedCommands}
          getRowKey={(c) => c.id}
          rowTestId={(c) => `command-row-${c.id}`}
          onRowClick={(c) => openEdit(c.id)}
          menuLabel={(c) => `More actions for ${c.name}`}
          rowActions={(c) => ({
            visible: [{ id: 'edit', label: 'Edit', ariaLabel: `Edit ${c.name}`, icon: 'edit', onSelect: () => openEdit(c.id) }],
            menu: [{ id: 'delete', label: 'Delete', ariaLabel: `Delete ${c.name}`, icon: 'delete', tone: 'danger', onSelect: () => del.open({ id: c.id, name: c.name }) }],
          })}
          state={listState}
        />
        {dialog}
      </section>
    );
  }

  const submitCreate = async () => {
    const validation = validate(form);
    if (validation) {
      setErrors(validation);
      return;
    }
    setSubmitting(true);
    try {
      const detectionResult = detectionToDto(form.detection);
      if (detectionResult && 'error' in detectionResult) {
        setErrors({ detection: detectionResult.error ?? 'Invalid detection target' });
        return;
      }
      await createCommand({
        name: form.name.trim(),
        steps: stepsToDto(form.steps),
        detection: detectionResult?.value ?? undefined,
        ...(form.parameters && form.parameters.length > 0 ? { parameters: form.parameters } : {}),
      });
      setCreating(false);
      setForm(emptyForm);
      setDirty(false);
      setTableMessage('Command created.');
      await reloadCommands();
    } catch (err: any) {
      // Feature 078 (FR-029): a parameter rejection carries the offending field and name, so
      // show that rather than a bare "Failed to create command".
      setErrors(toSaveErrors(err, 'Failed to create command'));
    } finally {
      setSubmitting(false);
    }
  };

  const submitUpdate = async () => {
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
        setErrors({ detection: detectionResult.error ?? 'Invalid detection target' });
        return;
      }
      await updateCommand(editingId, {
        name: form.name.trim(),
        steps: stepsToDto(form.steps),
        detection: detectionResult?.value ?? undefined,
        // Always sent on update so clearing the last declaration actually persists.
        parameters: form.parameters ?? [],
      });
      await reloadCommands();
      setEditingId(undefined);
      setForm(emptyForm);
      setDirty(false);
      setTableMessage('Command updated.');
    } catch (err: any) {
      setErrors(toSaveErrors(err, 'Failed to update command'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section>
      <PageHeader onBack={backToList} title={creating ? 'New command' : loadedName} />
      <div className="ui-form">
        <CommandForm
          value={form}
          commandOptions={creating ? commandOptions : filteredCommandOptions}
          errors={errors}
          stepFieldErrors={stepFieldErrors}
          submitting={submitting}
          loading={loadingCommands}
          onChange={(v) => { setErrors(undefined); setStepFieldErrors(undefined); setForm(v); setDirty(true); }}
          onCancel={backToList}
          onSubmit={creating ? submitCreate : submitUpdate}
          onDelete={editingId ? () => del.open({ id: editingId, name: loadedName }) : undefined}
          deleting={del.busy}
        />
      </div>
      {dialog}
    </section>
  );
};
