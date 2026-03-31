import React, { useEffect, useMemo, useState } from 'react';
import { listSequences, SequenceDto, createSequence, getSequence, updateSequence, deleteSequence, isSequenceConflictError } from '../services/sequences';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listCommands, CommandDto } from '../services/commands';
import { FormError } from '../components/Form';
import { FormActions, FormSection } from '../components/unified/FormLayout';
import { SearchableDropdown, SearchableOption } from '../components/SearchableDropdown';
import { ReorderableList, ReorderableListItem } from '../components/ReorderableList';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';
import { navigateToUnified } from '../lib/navigation';
import { validatePerStepConditions } from '../lib/validation';
import { isLinearStepArray, toCommandStepIds, toLinearSteps } from '../lib/sequenceMapping';
import { LoopBlock } from '../components/sequences/LoopBlock';
import type { LoopStepEntry, BreakStepEntry, StepEntry } from '../types/stepEntry';
import type { SequenceLinearStep, LoopConfigDto } from '../types/sequenceFlow';

type SequenceStep = {
  id: string;
  stepId: string;
  stepType: 'Action' | 'Loop' | 'Break';
  commandId: string;
  conditionType: 'none' | 'imageVisible' | 'commandOutcome';
  conditionNegate: boolean;
  imageId: string;
  minSimilarity: string;
  outcomeStepRef: string;
  expectedState: 'success' | 'failed' | 'skipped';
  loopEntry?: LoopStepEntry;
};

type SequenceFormValue = {
  name: string;
  steps: SequenceStep[];
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: SequenceFormValue = { name: '', steps: [] };

const createDefaultStep = (commandId: string, stepId: string): SequenceStep => ({
  id: makeId(),
  stepId,
  stepType: 'Action',
  commandId,
  conditionType: 'none',
  conditionNegate: false,
  imageId: '',
  minSimilarity: '',
  outcomeStepRef: '',
  expectedState: 'success'
});

const toStepEntries = (ids?: string[]): SequenceStep[] => (ids ?? []).map((cmdId, index) => createDefaultStep(cmdId, `step-${index + 1}`));

const nextGeneratedStepId = (steps: SequenceStep[]): string => {
  const highest = steps.reduce((max, step) => {
    const match = /^step-(\d+)$/i.exec(step.stepId.trim());
    if (!match) {
      return max;
    }

    const parsed = Number(match[1]);
    return Number.isFinite(parsed) ? Math.max(max, parsed) : max;
  }, 0);

  return `step-${highest + 1}`;
};

const linearBodyToStepEntries = (body: SequenceLinearStep[]): StepEntry[] => {
  return body.map<StepEntry>((child) => {
    if (child.stepType === 'Break') {
      return {
        type: 'Break',
        id: makeId(),
        stepId: child.stepId,
        breakCondition: child.breakCondition ?? undefined
      } satisfies BreakStepEntry;
    }
    const cmdId = typeof child.action?.parameters?.commandId === 'string'
      ? child.action.parameters.commandId
      : child.stepId;
    return {
      type: 'Action',
      id: makeId(),
      stepId: child.stepId,
      commandId: cmdId,
      conditionType: child.condition?.type === 'imageVisible' ? 'imageVisible'
        : child.condition?.type === 'commandOutcome' ? 'commandOutcome' : 'none',
      conditionNegate: child.condition?.negate ?? false,
      imageId: child.condition?.type === 'imageVisible' ? child.condition.imageId : '',
      minSimilarity: child.condition?.type === 'imageVisible' && child.condition.minSimilarity != null ? String(child.condition.minSimilarity) : '',
      outcomeStepRef: child.condition?.type === 'commandOutcome' ? child.condition.stepRef : '',
      expectedState: child.condition?.type === 'commandOutcome' ? child.condition.expectedState : 'success'
    };
  });
};

const loopDtoToEntry = (step: SequenceLinearStep): LoopStepEntry => {
  const loop = step.loop!;
  const base: Pick<LoopStepEntry, 'type' | 'id' | 'stepId'> = { type: 'Loop', id: makeId(), stepId: step.stepId };
  const body = linearBodyToStepEntries(step.body ?? []);
  if (loop.loopType === 'count') {
    return { ...base, loopType: 'count', count: loop.count, maxIterations: loop.maxIterations ?? undefined, body };
  }
  if (loop.loopType === 'while') {
    return { ...base, loopType: 'while', condition: loop.condition, maxIterations: loop.maxIterations ?? undefined, body };
  }
  // repeatUntil
  return { ...base, loopType: 'repeatUntil', condition: loop.condition, maxIterations: loop.maxIterations ?? undefined, body };
};

const toStepEntriesFromLinear = (steps: SequenceLinearStep[]): SequenceStep[] => {
  return steps.map((step) => {
    if (step.stepType === 'Loop' && step.loop) {
      const loopEntry = loopDtoToEntry(step);
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Loop' as const,
        commandId: '',
        conditionType: 'none' as const,
        conditionNegate: false,
        imageId: '',
        minSimilarity: '',
        outcomeStepRef: '',
        expectedState: 'success' as const,
        loopEntry
      };
    }

    if (step.stepType === 'Break') {
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Break' as const,
        commandId: '',
        conditionType: 'none' as const,
        conditionNegate: false,
        imageId: '',
        minSimilarity: '',
        outcomeStepRef: '',
        expectedState: 'success' as const
      };
    }

    const commandId = typeof step.action?.parameters?.commandId === 'string'
      ? step.action.parameters.commandId
      : step.stepId;

    if (step.condition?.type === 'imageVisible') {
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Action' as const,
        commandId,
        conditionType: 'imageVisible',
        conditionNegate: step.condition.negate ?? false,
        imageId: step.condition.imageId,
        minSimilarity: step.condition.minSimilarity == null ? '' : String(step.condition.minSimilarity),
        outcomeStepRef: '',
        expectedState: 'success'
      };
    }

    if (step.condition?.type === 'commandOutcome') {
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Action' as const,
        commandId,
        conditionType: 'commandOutcome',
        conditionNegate: step.condition.negate ?? false,
        imageId: '',
        minSimilarity: '',
        outcomeStepRef: step.condition.stepRef,
        expectedState: step.condition.expectedState
      };
    }

    return {
      ...createDefaultStep(commandId, step.stepId),
      stepId: step.stepId
    };
  });
};

const buildConditionPayload = (step: SequenceStep) => {
  if (step.conditionType === 'imageVisible') {
    return {
      type: 'imageVisible' as const,
      imageId: step.imageId.trim(),
      minSimilarity: step.minSimilarity.trim() === '' ? null : Number(step.minSimilarity),
      negate: step.conditionNegate || undefined
    };
  }
  if (step.conditionType === 'commandOutcome') {
    return {
      type: 'commandOutcome' as const,
      stepRef: step.outcomeStepRef.trim(),
      expectedState: step.expectedState,
      negate: step.conditionNegate || undefined
    };
  }
  return null;
};

const buildLoopConfigPayload = (loop: LoopStepEntry): LoopConfigDto | null => {
  if (loop.loopType === 'count') {
    return { loopType: 'count', count: loop.count ?? 1, maxIterations: loop.maxIterations ?? null };
  }
  if (loop.loopType === 'while' && loop.condition) {
    return { loopType: 'while', condition: loop.condition, maxIterations: loop.maxIterations ?? null };
  }
  if (loop.loopType === 'repeatUntil' && loop.condition) {
    return { loopType: 'repeatUntil', condition: loop.condition, maxIterations: loop.maxIterations ?? null };
  }
  return null;
};

const bodyEntryToPayloadStep = (entry: StepEntry): SequenceLinearStep => {
  if (entry.type === 'Break') {
    const br = entry as BreakStepEntry;
    return {
      stepId: br.stepId.trim(),
      stepType: 'Break',
      breakCondition: br.breakCondition ?? null
    };
  }
  // Action body step
  const a = entry as { stepId: string; commandId: string; conditionType: string; conditionNegate: boolean; imageId: string; minSimilarity: string; outcomeStepRef: string; expectedState: 'success' | 'failed' | 'skipped' };
  const cond = a.conditionType === 'imageVisible'
    ? { type: 'imageVisible' as const, imageId: a.imageId.trim(), minSimilarity: a.minSimilarity.trim() === '' ? null : Number(a.minSimilarity), negate: a.conditionNegate || undefined }
    : a.conditionType === 'commandOutcome'
      ? { type: 'commandOutcome' as const, stepRef: a.outcomeStepRef.trim(), expectedState: a.expectedState, negate: a.conditionNegate || undefined }
      : null;
  return {
    stepId: entry.stepId.trim(),
    stepType: 'Action',
    action: { type: 'command', parameters: { commandId: a.commandId } },
    condition: cond
  };
};

const toLinearPayloadSteps = (steps: SequenceStep[]): SequenceLinearStep[] => {
  return steps.map((step) => {
    if (step.stepType === 'Loop' && step.loopEntry) {
      return {
        stepId: step.stepId.trim(),
        stepType: 'Loop' as const,
        loop: buildLoopConfigPayload(step.loopEntry),
        body: step.loopEntry.body.map(bodyEntryToPayloadStep)
      };
    }

    if (step.stepType === 'Break') {
      return {
        stepId: step.stepId.trim(),
        stepType: 'Break' as const,
        breakCondition: null // top-level breaks are unconditional
      };
    }

    return {
      stepId: step.stepId.trim(),
      stepType: 'Action' as const,
      action: {
        type: 'command',
        parameters: {
          commandId: step.commandId
        }
      },
      condition: buildConditionPayload(step)
    };
  });
};

type SequencesPageProps = {
  initialCreate?: boolean;
  initialEditId?: string;
};

export const SequencesPage: React.FC<SequencesPageProps> = ({ initialCreate, initialEditId }) => {
  const [sequences, setSequences] = useState<SequenceDto[]>([]);
  const [creating, setCreating] = useState(Boolean(initialCreate));
  const [commandOptions, setCommandOptions] = useState<SearchableOption[]>([]);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [form, setForm] = useState<SequenceFormValue>(emptyForm);
  const [pendingStepId, setPendingStepId] = useState<string | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [filterName, setFilterName] = useState('');
  const [tableMessage, setTableMessage] = useState<string | undefined>(undefined);
  const [tableError, setTableError] = useState<string | undefined>(undefined);
  const [loadedVersion, setLoadedVersion] = useState(1);

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    Promise.all([listSequences(), listCommands()])
      .then(([seqs, cmds]: [SequenceDto[], CommandDto[]]) => {
        if (!mounted) return;
        setSequences(seqs);
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
        setTableError(undefined);
      })
      .catch((err: any) => {
        if (!mounted) return;
        setSequences([]);
        setCommandOptions([]);
        setTableError(err?.message ?? 'Failed to load sequences');
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);

  const commandLookup = useMemo(() => new Map(commandOptions.map((o) => [o.value, o.label])), [commandOptions]);
  const sequenceRows = useMemo(() => sequences.map((s) => ({ id: s.id, name: s.name, stepCount: s.steps?.length ?? 0 })), [sequences]);

  const displayedSequences = useMemo(() => {
    const query = filterName.trim().toLowerCase();
    return sequenceRows
      .filter((s) => !query || s.name.toLowerCase().includes(query))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [sequenceRows, filterName]);

  const reloadSequences = async () => {
    setLoading(true);
    try {
      const data = await listSequences();
      setSequences(data);
      setTableError(undefined);
    } catch (err: any) {
      setSequences([]);
      setTableError(err?.message ?? 'Failed to load sequences');
    } finally {
      setLoading(false);
    }
  };

  const loadSequenceIntoForm = async (id: string) => {
    const s = await getSequence(id);
    const commandIds = toCommandStepIds(s.steps);
    const linearSteps = toLinearSteps(s.steps);
    const hasPerStep = isLinearStepArray(s.steps) && linearSteps.length > 0;
    setEditingId(id);
    setCreating(false);
    setPendingStepId(undefined);
    setForm({ name: s.name, steps: hasPerStep ? toStepEntriesFromLinear(linearSteps) : toStepEntries(commandIds) });
    setLoadedVersion(s.version ?? 1);
    setDirty(false);
  };

  const resetForm = () => {
    setForm(emptyForm);
    setPendingStepId(undefined);
    setLoadedVersion(1);
    setErrors(undefined);
    setDirty(false);
  };

  const renderStepConditionEditor = (step: SequenceStep, index: number): React.ReactNode => (
    <div className="sequence-step-condition-editor">
      <div className="sequence-step-condition-field sequence-step-condition-field--type">
        <label htmlFor={`step-condition-type-${step.id}`}>Condition Type</label>
        <select
          id={`step-condition-type-${step.id}`}
          value={step.conditionType}
          onChange={(event) => {
            const value = event.target.value as SequenceStep['conditionType'];
            setForm((prev) => ({
              ...prev,
              steps: prev.steps.map((candidate) => candidate.id === step.id
                ? {
                  ...candidate,
                  conditionType: value,
                  imageId: value === 'imageVisible' ? candidate.imageId : '',
                  minSimilarity: value === 'imageVisible' ? candidate.minSimilarity : '',
                  outcomeStepRef: value === 'commandOutcome' ? candidate.outcomeStepRef : '',
                  expectedState: value === 'commandOutcome' ? candidate.expectedState : 'success'
                }
                : candidate)
            }));
            setDirty(true);
          }}
          disabled={submitting || loading}
        >
          <option value="none">None</option>
          <option value="imageVisible">imageVisible</option>
          <option value="commandOutcome">commandOutcome</option>
        </select>
      </div>

      {step.conditionType !== 'none' && (
        <div className="sequence-step-condition-field sequence-step-condition-field--negate">
          <label>
            <input
              type="checkbox"
              data-testid={`step-condition-negate-${step.id}`}
              checked={step.conditionNegate}
              onChange={(e) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, conditionNegate: e.target.checked } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            />
            NOT
          </label>
        </div>
      )}

      {step.conditionType === 'imageVisible' && (
        <>
          <div className="sequence-step-condition-field sequence-step-condition-field--image-id">
            <label htmlFor={`step-image-id-${step.id}`}>Image Id</label>
            <input
              id={`step-image-id-${step.id}`}
              value={step.imageId}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, imageId: event.target.value } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            />
          </div>

          <div className="sequence-step-condition-field sequence-step-condition-field--min-similarity">
            <label htmlFor={`step-min-similarity-${step.id}`}>Min Similarity</label>
            <input
              id={`step-min-similarity-${step.id}`}
              value={step.minSimilarity}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, minSimilarity: event.target.value } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            />
          </div>
        </>
      )}

      {step.conditionType === 'commandOutcome' && (
        <>
          <div className="sequence-step-condition-field sequence-step-condition-field--step-ref">
            <label htmlFor={`step-step-ref-${step.id}`}>Step Ref</label>
            <select
              id={`step-step-ref-${step.id}`}
              value={step.outcomeStepRef}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, outcomeStepRef: event.target.value } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            >
              <option value="">Select prior step</option>
              {form.steps.slice(0, index).map((candidate, candidateIndex) => (
                <option key={candidate.id} value={candidate.stepId}>{`Step ${candidateIndex + 1}`}</option>
              ))}
            </select>
          </div>

          <div className="sequence-step-condition-field sequence-step-condition-field--expected-state">
            <label htmlFor={`step-expected-state-${step.id}`}>Expected State</label>
            <select
              id={`step-expected-state-${step.id}`}
              value={step.expectedState}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id
                    ? { ...candidate, expectedState: event.target.value as SequenceStep['expectedState'] }
                    : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            >
              <option value="success">success</option>
              <option value="failed">failed</option>
              <option value="skipped">skipped</option>
            </select>
          </div>
        </>
      )}
    </div>
  );

  const createLoopStep = (loopType: 'count' | 'while' | 'repeatUntil'): SequenceStep => {
    const id = makeId();
    const stepId = nextGeneratedStepId(form.steps);
    const loopEntry: LoopStepEntry = {
      type: 'Loop',
      id,
      stepId,
      loopType,
      count: loopType === 'count' ? 3 : undefined,
      condition: loopType !== 'count' ? { type: 'imageVisible', imageId: '', minSimilarity: null } : undefined,
      body: [],
    };
    return {
      id,
      stepId,
      stepType: 'Loop',
      commandId: '',
      conditionType: 'none',
      conditionNegate: false,
      imageId: '',
      minSimilarity: '',
      outcomeStepRef: '',
      expectedState: 'success',
      loopEntry,
    };
  };

  const stepItems = useMemo<ReorderableListItem[]>(() => {
    return form.steps.map((s, idx) => {
      if (s.stepType === 'Loop' && s.loopEntry) {
        return {
          id: s.id,
          label: `Loop (${s.loopEntry.loopType})`,
          details: (
            <LoopBlock
              loop={s.loopEntry}
              onChange={(updated) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((step) =>
                    step.id === s.id ? { ...step, loopEntry: updated } : step
                  ),
                }));
                setDirty(true);
              }}
              onRemove={() => {
                setForm((prev) => ({ ...prev, steps: prev.steps.filter((step) => step.id !== s.id) }));
                setDirty(true);
              }}
              commandOptions={commandOptions}
              disabled={submitting || loading}
            />
          ),
        };
      }
      return {
        id: s.id,
        label: commandLookup.get(s.commandId) ?? s.commandId,
        details: renderStepConditionEditor(s, idx),
      };
    });
  }, [form.steps, commandOptions, commandLookup, submitting, loading]);

  const validate = (v: SequenceFormValue): Record<string, string> | undefined => {
    const next: Record<string, string> = {};
    if (!v.name.trim()) next.name = 'Name is required';
    return Object.keys(next).length ? next : undefined;
  };

  useEffect(() => {
    if (!initialEditId) return;
    const load = async () => {
      try {
        setErrors(undefined);
        await loadSequenceIntoForm(initialEditId);
      } catch (err: any) {
        setErrors({ form: err?.message ?? 'Failed to load sequence' });
      }
    };
    void load();
  }, [initialEditId]);

  return (
    <section>
      <h2>Sequences</h2>
      {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
      {tableError && <div className="form-error" role="alert">{tableError}</div>}
      <div className="actions-header">
        <button
          onClick={() => {
            if (!confirmNavigate()) return;
            setCreating(true);
            setEditingId(undefined);
            setErrors(undefined);
            resetForm();
          }}
        >
          Create Sequence
        </button>
      </div>
      {!loading && sequences.length === 0 && (
        <div className="form-hint" role="status">No sequences yet. Create your first sequence to start authoring automation flow.</div>
      )}
      <table className="sequences-table" aria-label="Sequences table">
        <thead>
          <tr>
            <th>
              <div>Name</div>
              <input
                aria-label="Filter by name"
                value={filterName}
                onChange={(e) => setFilterName(e.target.value)}
                placeholder="Filter by name"
                disabled={loading}
              />
            </th>
            <th>
              <div>Steps</div>
            </th>
          </tr>
        </thead>
        <tbody>
          {loading && (
            <tr><td colSpan={2}>Loading...</td></tr>
          )}
          {!loading && displayedSequences.length === 0 && (
            <tr><td colSpan={2}>No sequences found.</td></tr>
          )}
          {!loading && displayedSequences.length > 0 && displayedSequences.map((s) => (
            <tr key={s.id} className="sequences-row">
              <td>
                <button
                  type="button"
                  className="link-button"
                  onClick={async () => {
                    if (!confirmNavigate()) return;
                    try {
                      await loadSequenceIntoForm(s.id);
                    } catch (err: any) {
                      setErrors({ form: err?.message ?? 'Failed to load sequence' });
                    }
                  }}
                >
                  {s.name}
                </button>
              </td>
              <td>{s.stepCount}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {creating && (
        <form
          className="edit-form"
          onSubmit={async (e) => {
            e.preventDefault();
            const validation = validate(form);
            if (validation) {
              setErrors(validation);
              return;
            }

            const linearPayload = {
              name: form.name.trim(),
              version: 1,
              steps: toLinearPayloadSteps(form.steps)
            };

            if (linearPayload) {
              const linearErrors = validatePerStepConditions(linearPayload.steps);
              if (linearErrors.length > 0) {
                setErrors({ form: linearErrors[0] });
                return;
              }
            }

            setSubmitting(true);
            try {
              await createSequence(
                {
                  name: linearPayload.name,
                  version: linearPayload.version,
                  steps: linearPayload.steps
                }
              );
              setCreating(false);
              setForm(emptyForm);
              setPendingStepId(undefined);
              setDirty(false);
              setTableMessage('Sequence created successfully.');
              await reloadSequences();
            } catch (err: any) {
              setErrors({ form: err?.message ?? 'Failed to create sequence' });
            } finally {
              setSubmitting(false);
            }
          }}
        >
          <FormSection title="Basics" description="Primary details for the sequence." id="sequence-basics">
            <div className="field">
              <label htmlFor="sequence-name">Name *</label>
              <input
                id="sequence-name"
                value={form.name}
                onChange={(e) => { setErrors(undefined); setForm({ ...form, name: e.target.value }); setDirty(true); }}
                aria-invalid={Boolean(errors?.name)}
                aria-describedby={errors?.name ? 'sequence-name-error' : undefined}
                disabled={submitting || loading}
              />
              {errors?.name && <div id="sequence-name-error" className="field-error" role="alert">{errors.name}</div>}
            </div>
          </FormSection>

          <FormSection title="Steps" description="Add commands in the order they should run and configure conditions inline." id="sequence-steps">
            <SearchableDropdown
              id="sequence-step-dropdown"
              label="Add command"
              options={commandOptions}
              value={pendingStepId}
              onChange={(val) => { setPendingStepId(val); setErrors(undefined); }}
              disabled={submitting || loading}
              placeholder="Select a command"
              onCreateNew={() => navigateToUnified('Commands', { create: true, newTab: true })}
              createLabel="Create new command"
            />
            <div className="field">
              <button type="button" onClick={() => {
                if (!pendingStepId) return;
                const next = [...form.steps, createDefaultStep(pendingStepId, nextGeneratedStepId(form.steps))];
                setForm({ ...form, steps: next });
                setPendingStepId(undefined);
                setDirty(true);
              }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
            </div>
            <div className="field" data-testid="add-loop-buttons">
              <span>Add loop:</span>{' '}
              <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('count')] })); setDirty(true); }} disabled={submitting || loading}>Count</button>{' '}
              <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('while')] })); setDirty(true); }} disabled={submitting || loading}>While</button>{' '}
              <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('repeatUntil')] })); setDirty(true); }} disabled={submitting || loading}>Repeat‑Until</button>
            </div>
            <ReorderableList
              items={stepItems}
              onChange={(next) => {
                const mapped = next.map((item, idx) => form.steps.find((s) => s.id === item.id) ?? form.steps[idx]);
                setForm({ ...form, steps: mapped.filter((s): s is SequenceStep => !!s?.commandId || s?.stepType === 'Loop') });
                setDirty(true);
              }}
              onDelete={(item) => {
                setForm({ ...form, steps: form.steps.filter((s) => s.id !== item.id) });
                setDirty(true);
              }}
              disabled={submitting || loading}
              emptyMessage="No commands added yet."
            />
            <div className="form-hint">Steps execute in listed order; drag buttons to reorder before saving.</div>
          </FormSection>


          <FormActions submitting={submitting} onCancel={() => { if (!confirmNavigate()) return; setCreating(false); resetForm(); }}>
            {loading && <span className="form-hint">Loading…</span>}
          </FormActions>
          <FormError message={errors?.form} />
        </form>
      )}
      {editingId && (
        <section>
          <h3>Edit Sequence</h3>
          <form
            className="edit-form"
            onSubmit={async (e) => {
              e.preventDefault();
              if (!editingId) return;
              const validation = validate(form);
              if (validation) {
                setErrors(validation);
                return;
              }

              const linearPayload = {
                name: form.name.trim(),
                version: loadedVersion,
                steps: toLinearPayloadSteps(form.steps)
              };

              if (linearPayload) {
                const linearErrors = validatePerStepConditions(linearPayload.steps);
                if (linearErrors.length > 0) {
                  setErrors({ form: linearErrors[0] });
                  return;
                }
              }

              setSubmitting(true);
              try {
                await updateSequence(
                  editingId,
                  {
                    name: linearPayload.name,
                    version: linearPayload.version,
                    steps: linearPayload.steps
                  }
                );
                await reloadSequences();
                setEditingId(undefined);
                resetForm();
                setDirty(false);
                setTableMessage('Sequence updated successfully.');
              } catch (err: any) {
                if (isSequenceConflictError(err)) {
                  setErrors({ form: `Sequence has changed on the server (version ${err.payload.currentVersion}). Reloaded latest version.` });
                  await loadSequenceIntoForm(editingId);
                } else {
                  setErrors({ form: err?.message ?? 'Failed to update sequence' });
                }
              } finally {
                setSubmitting(false);
              }
            }}
          >
            <FormSection title="Basics" description="Primary details for the sequence." id="sequence-edit-basics">
              <div className="field">
                <label htmlFor="sequence-edit-name">Name *</label>
                <input
                  id="sequence-edit-name"
                  value={form.name}
                  onChange={(e) => { setErrors(undefined); setForm({ ...form, name: e.target.value }); setDirty(true); }}
                  aria-invalid={Boolean(errors?.name)}
                  aria-describedby={errors?.name ? 'sequence-edit-name-error' : undefined}
                  disabled={submitting || loading}
                />
                {errors?.name && <div id="sequence-edit-name-error" className="field-error" role="alert">{errors.name}</div>}
              </div>
            </FormSection>

            <FormSection title="Steps" description="Add commands in the order they should run and configure conditions inline." id="sequence-edit-steps">
              <SearchableDropdown
                id="sequence-edit-step-dropdown"
                label="Add command"
                options={commandOptions}
                value={pendingStepId}
                onChange={(val) => { setPendingStepId(val); setErrors(undefined); }}
                disabled={submitting || loading}
                placeholder="Select a command"
                onCreateNew={() => navigateToUnified('Commands', { create: true, newTab: true })}
                createLabel="Create new command"
              />
              <div className="field">
                <button type="button" onClick={() => {
                  if (!pendingStepId) return;
                  const next = [...form.steps, createDefaultStep(pendingStepId, nextGeneratedStepId(form.steps))];
                  setForm({ ...form, steps: next });
                  setPendingStepId(undefined);
                  setDirty(true);
                }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
              </div>
              <div className="field" data-testid="edit-add-loop-buttons">
                <span>Add loop:</span>{' '}
                <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('count')] })); setDirty(true); }} disabled={submitting || loading}>Count</button>{' '}
                <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('while')] })); setDirty(true); }} disabled={submitting || loading}>While</button>{' '}
                <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('repeatUntil')] })); setDirty(true); }} disabled={submitting || loading}>Repeat‑Until</button>
              </div>
              <ReorderableList
                items={stepItems}
                onChange={(next) => {
                  const mapped = next.map((item, idx) => form.steps.find((s) => s.id === item.id) ?? form.steps[idx]);
                  setForm({ ...form, steps: mapped.filter((s): s is SequenceStep => !!s?.commandId || s?.stepType === 'Loop') });
                  setDirty(true);
                }}
                onDelete={(item) => {
                  setForm({ ...form, steps: form.steps.filter((s) => s.id !== item.id) });
                  setDirty(true);
                }}
                disabled={submitting || loading}
                emptyMessage="No commands added yet."
              />
              <div className="form-hint">Use Move up/down to set the execution order before saving.</div>
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
            await deleteSequence(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            setDirty(false);
            resetForm();
            await reloadSequences();
            setTableMessage('Sequence deleted successfully.');
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: sequence is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setErrors({ form: err?.message ?? 'Failed to delete sequence' });
            }
          }
        }}
      />
    </section>
  );
};

