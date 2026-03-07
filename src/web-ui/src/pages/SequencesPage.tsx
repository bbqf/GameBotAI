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
import { validateConditionalFlow, validatePerStepConditions } from '../lib/validation';
import { buildSequenceFlow, ConditionDraft, createDefaultConditionExpression } from '../lib/sequenceFlowGraph';
import { isFlowStepArray, isLinearStepArray, toCommandStepIds, toConditionDrafts, toLinearSteps } from '../lib/sequenceMapping';
import { ConditionExpressionBuilder } from '../components/authoring/ConditionExpressionBuilder';
import { SequenceBranchConnector } from '../components/authoring/SequenceBranchConnector';
import type { FlowStep, ConditionExpression, BranchLink, SequenceLinearStep } from '../types/sequenceFlow';

type SequenceStep = {
  id: string;
  stepId: string;
  commandId: string;
  conditionType: 'none' | 'imageVisible' | 'commandOutcome';
  imageId: string;
  minSimilarity: string;
  outcomeStepRef: string;
  expectedState: 'success' | 'failed' | 'skipped';
};

type SequenceFormValue = {
  name: string;
  steps: SequenceStep[];
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: SequenceFormValue = { name: '', steps: [] };

const createDefaultStep = (commandId: string, index: number): SequenceStep => ({
  id: makeId(),
  stepId: `step-${index + 1}`,
  commandId,
  conditionType: 'none',
  imageId: '',
  minSimilarity: '',
  outcomeStepRef: '',
  expectedState: 'success'
});

const toStepEntries = (ids?: string[]): SequenceStep[] => (ids ?? []).map((cmdId, index) => createDefaultStep(cmdId, index));

const toStepEntriesFromLinear = (steps: SequenceLinearStep[]): SequenceStep[] => {
  return steps.map((step, index) => {
    const commandId = typeof step.action?.parameters?.commandId === 'string'
      ? step.action.parameters.commandId
      : step.stepId;

    if (step.condition?.type === 'imageVisible') {
      return {
        id: makeId(),
        stepId: step.stepId,
        commandId,
        conditionType: 'imageVisible',
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
        commandId,
        conditionType: 'commandOutcome',
        imageId: '',
        minSimilarity: '',
        outcomeStepRef: step.condition.stepRef,
        expectedState: step.condition.expectedState
      };
    }

    return {
      ...createDefaultStep(commandId, index),
      stepId: step.stepId
    };
  });
};

const toPayloadSteps = (steps: SequenceStep[]) => steps.map((s) => s.commandId);

const toLinearPayloadSteps = (steps: SequenceStep[]): SequenceLinearStep[] => {
  return steps.map((step) => {
    const condition = step.conditionType === 'imageVisible'
      ? {
        type: 'imageVisible' as const,
        imageId: step.imageId.trim(),
        minSimilarity: step.minSimilarity.trim() === '' ? null : Number(step.minSimilarity)
      }
      : step.conditionType === 'commandOutcome'
        ? {
          type: 'commandOutcome' as const,
          stepRef: step.outcomeStepRef.trim(),
          expectedState: step.expectedState
        }
        : null;

    return {
      stepId: step.stepId.trim(),
      action: {
        type: 'command',
        parameters: {
          commandId: step.commandId
        }
      },
      condition
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
  const [conditionalEnabled, setConditionalEnabled] = useState(false);
  const [perStepEnabled, setPerStepEnabled] = useState(false);
  const [entryStepId, setEntryStepId] = useState('');
  const [conditions, setConditions] = useState<ConditionDraft[]>([]);
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
    const flowSteps = isFlowStepArray(s.steps) ? s.steps : [];
    const links = Array.isArray(s.links) ? s.links : [];
    const hasConditionalFlow = !!s.entryStepId && flowSteps.length > 0 && links.length > 0;
    const hasPerStep = isLinearStepArray(s.steps) && linearSteps.length > 0;
    setEditingId(id);
    setCreating(false);
    setPendingStepId(undefined);
    setForm({ name: s.name, steps: hasPerStep ? toStepEntriesFromLinear(linearSteps) : toStepEntries(commandIds) });
    setLoadedVersion(s.version ?? 1);
    setPerStepEnabled(hasPerStep);
    setConditionalEnabled(hasConditionalFlow && !hasPerStep);
    setEntryStepId(hasConditionalFlow ? s.entryStepId! : (commandIds[0] ?? ''));
    setConditions(hasConditionalFlow ? toConditionDrafts(flowSteps, links, commandIds[0] ?? '') : []);
    setDirty(false);
  };

  const resetForm = () => {
    setForm(emptyForm);
    setPendingStepId(undefined);
    setPerStepEnabled(false);
    setConditionalEnabled(false);
    setEntryStepId('');
    setConditions([]);
    setLoadedVersion(1);
    setErrors(undefined);
    setDirty(false);
  };

  const commandStepIds = useMemo(() => form.steps.map((step) => step.commandId), [form.steps]);

  const stepItems = useMemo<ReorderableListItem[]>(() => {
    return form.steps.map((s, idx) => ({
      id: s.id,
      label: commandLookup.get(s.commandId) ?? s.commandId,
      description: `Step ${idx + 1}`
    }));
  }, [form.steps, commandLookup]);

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

            const conditionalPayload = conditionalEnabled
              ? buildSequenceFlow(
                form.name.trim(),
                1,
                commandStepIds,
                entryStepId || commandStepIds[0] || '',
                conditions)
              : null;

            const linearPayload = perStepEnabled
              ? {
                name: form.name.trim(),
                version: 1,
                steps: toLinearPayloadSteps(form.steps)
              }
              : null;

            if (conditionalPayload) {
              const conditionalErrors = validateConditionalFlow(conditionalPayload);
              if (conditionalErrors.length > 0) {
                setErrors({ form: conditionalErrors[0] });
                return;
              }
            }

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
                conditionalPayload
                  ? {
                    name: conditionalPayload.name,
                    version: conditionalPayload.version,
                    entryStepId: conditionalPayload.entryStepId,
                    steps: conditionalPayload.steps,
                    links: conditionalPayload.links
                  }
                  : linearPayload
                    ? {
                      name: linearPayload.name,
                      version: linearPayload.version,
                      steps: linearPayload.steps
                    }
                  : { name: form.name.trim(), steps: toPayloadSteps(form.steps) }
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

          <FormSection title="Steps" description="Add commands in the order they should run." id="sequence-steps">
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
                const next = [...form.steps, createDefaultStep(pendingStepId, form.steps.length)];
                setForm({ ...form, steps: next });
                setPendingStepId(undefined);
                setDirty(true);
              }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
            </div>
            <ReorderableList
              items={stepItems}
              onChange={(next) => {
                const mapped = next.map((item, idx) => form.steps.find((s) => s.id === item.id) ?? form.steps[idx]);
                setForm({ ...form, steps: mapped.filter((s): s is SequenceStep => !!s?.commandId) });
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

          <FormSection title="Conditional Flow" description="Optional branching by condition." id="sequence-conditional-flow">
            <div className="field">
              <label htmlFor="conditional-flow-enabled">
                <input
                  id="conditional-flow-enabled"
                  aria-label="Enable conditional flow"
                  type="checkbox"
                  checked={conditionalEnabled}
                  onChange={(event) => {
                    setConditionalEnabled(event.target.checked);
                    if (event.target.checked) {
                      setPerStepEnabled(false);
                    }
                    if (event.target.checked && !entryStepId) {
                      setEntryStepId(commandStepIds[0] ?? '');
                    }
                    setDirty(true);
                  }}
                  disabled={submitting || loading}
                />
                Enable conditional flow
              </label>
            </div>

            {conditionalEnabled && (
              <>
                <div className="field">
                  <label htmlFor="sequence-entry-step">Entry Step</label>
                  <select
                    id="sequence-entry-step"
                    aria-label="Entry Step"
                    value={entryStepId}
                    onChange={(event) => {
                      setEntryStepId(event.target.value);
                      setDirty(true);
                    }}
                  >
                    <option value="">Select entry step</option>
                    {commandStepIds.map((commandId) => (
                      <option key={commandId} value={commandId}>{commandLookup.get(commandId) ?? commandId}</option>
                    ))}
                  </select>
                </div>

                <div className="field">
                  <button
                    type="button"
                    onClick={() => {
                      const nextIndex = conditions.length + 1;
                      setConditions((prev) => [
                        ...prev,
                        {
                          stepId: `condition-${nextIndex}`,
                          sourceStepId: entryStepId || commandStepIds[0] || '',
                          trueTargetId: '',
                          falseTargetId: '',
                          expression: createDefaultConditionExpression()
                        }
                      ]);
                      setDirty(true);
                    }}
                    disabled={submitting || loading || commandStepIds.length === 0}
                  >
                    Add Condition Step
                  </button>
                </div>

                {conditions.map((condition, index) => (
                  <div key={index} className="field">
                    <label htmlFor={`condition-step-id-${index}`}>Condition Step Id</label>
                    <input
                      id={`condition-step-id-${index}`}
                      aria-label="Condition Step Id"
                      value={condition.stepId}
                      onChange={(event) => {
                        setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, stepId: event.target.value } : item));
                        setDirty(true);
                      }}
                    />

                    <SequenceBranchConnector
                      options={commandStepIds.map((stepId) => ({ value: stepId, label: commandLookup.get(stepId) ?? stepId }))}
                      trueTargetId={condition.trueTargetId}
                      falseTargetId={condition.falseTargetId}
                      onTrueTargetChange={(value) => {
                        setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, trueTargetId: value } : item));
                        setDirty(true);
                      }}
                      onFalseTargetChange={(value) => {
                        setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, falseTargetId: value } : item));
                        setDirty(true);
                      }}
                    />

                    <ConditionExpressionBuilder
                      value={condition.expression}
                      onChange={(value) => {
                        setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, expression: value } : item));
                        setDirty(true);
                      }}
                    />
                  </div>
                ))}
              </>
            )}
          </FormSection>

          <FormSection title="Per-Step Conditions" description="Optional conditions evaluated before each step." id="sequence-per-step-conditions">
            <div className="field">
              <label htmlFor="per-step-conditions-enabled">
                <input
                  id="per-step-conditions-enabled"
                  aria-label="Enable per-step conditions"
                  type="checkbox"
                  checked={perStepEnabled}
                  onChange={(event) => {
                    setPerStepEnabled(event.target.checked);
                    if (event.target.checked) {
                      setConditionalEnabled(false);
                    }
                    setDirty(true);
                  }}
                  disabled={submitting || loading}
                />
                Enable per-step conditions
              </label>
            </div>

            {perStepEnabled && form.steps.map((step, index) => (
              <div key={`create-per-step-${step.id}`} className="field">
                <label htmlFor={`create-step-id-${step.id}`}>Step Id ({step.commandId})</label>
                <input
                  id={`create-step-id-${step.id}`}
                  aria-label={`Step Id (${step.commandId})`}
                  value={step.stepId}
                  onChange={(event) => {
                    setForm((prev) => ({
                      ...prev,
                      steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, stepId: event.target.value } : candidate)
                    }));
                    setDirty(true);
                  }}
                />

                <label htmlFor={`create-step-condition-type-${step.id}`}>Condition Type ({step.commandId})</label>
                <select
                  id={`create-step-condition-type-${step.id}`}
                  aria-label={`Condition Type (${step.commandId})`}
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
                >
                  <option value="none">None</option>
                  <option value="imageVisible">imageVisible</option>
                  <option value="commandOutcome">commandOutcome</option>
                </select>

                {step.conditionType === 'imageVisible' && (
                  <>
                    <label htmlFor={`create-step-image-id-${step.id}`}>Image Id ({step.commandId})</label>
                    <input
                      id={`create-step-image-id-${step.id}`}
                      aria-label={`Image Id (${step.commandId})`}
                      value={step.imageId}
                      onChange={(event) => {
                        setForm((prev) => ({
                          ...prev,
                          steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, imageId: event.target.value } : candidate)
                        }));
                        setDirty(true);
                      }}
                    />

                    <label htmlFor={`create-step-min-similarity-${step.id}`}>Min Similarity ({step.commandId})</label>
                    <input
                      id={`create-step-min-similarity-${step.id}`}
                      aria-label={`Min Similarity (${step.commandId})`}
                      value={step.minSimilarity}
                      onChange={(event) => {
                        setForm((prev) => ({
                          ...prev,
                          steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, minSimilarity: event.target.value } : candidate)
                        }));
                        setDirty(true);
                      }}
                    />
                  </>
                )}

                {step.conditionType === 'commandOutcome' && (
                  <>
                    <label htmlFor={`create-step-step-ref-${step.id}`}>Step Ref ({step.commandId})</label>
                    <select
                      id={`create-step-step-ref-${step.id}`}
                      aria-label={`Step Ref (${step.commandId})`}
                      value={step.outcomeStepRef}
                      onChange={(event) => {
                        setForm((prev) => ({
                          ...prev,
                          steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, outcomeStepRef: event.target.value } : candidate)
                        }));
                        setDirty(true);
                      }}
                    >
                      <option value="">Select prior step</option>
                      {form.steps.slice(0, index).map((candidate) => (
                        <option key={candidate.id} value={candidate.stepId}>{candidate.stepId}</option>
                      ))}
                    </select>

                    <label htmlFor={`create-step-expected-state-${step.id}`}>Expected State ({step.commandId})</label>
                    <select
                      id={`create-step-expected-state-${step.id}`}
                      aria-label={`Expected State (${step.commandId})`}
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
                    >
                      <option value="success">success</option>
                      <option value="failed">failed</option>
                      <option value="skipped">skipped</option>
                    </select>
                  </>
                )}
              </div>
            ))}
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

              const conditionalPayload = conditionalEnabled
                ? buildSequenceFlow(
                  form.name.trim(),
                  loadedVersion,
                  commandStepIds,
                  entryStepId || commandStepIds[0] || '',
                  conditions)
                : null;

              const linearPayload = perStepEnabled
                ? {
                  name: form.name.trim(),
                  version: loadedVersion,
                  steps: toLinearPayloadSteps(form.steps)
                }
                : null;

              if (conditionalPayload) {
                const conditionalErrors = validateConditionalFlow(conditionalPayload);
                if (conditionalErrors.length > 0) {
                  setErrors({ form: conditionalErrors[0] });
                  return;
                }
              }

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
                  conditionalPayload
                    ? {
                      name: conditionalPayload.name,
                      version: conditionalPayload.version,
                      entryStepId: conditionalPayload.entryStepId,
                      steps: conditionalPayload.steps,
                      links: conditionalPayload.links
                    }
                    : linearPayload
                      ? {
                        name: linearPayload.name,
                        version: linearPayload.version,
                        steps: linearPayload.steps
                      }
                    : { name: form.name.trim(), steps: toPayloadSteps(form.steps) }
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

            <FormSection title="Steps" description="Add commands in the order they should run." id="sequence-edit-steps">
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
                  const next = [...form.steps, createDefaultStep(pendingStepId, form.steps.length)];
                  setForm({ ...form, steps: next });
                  setPendingStepId(undefined);
                  setDirty(true);
                }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
              </div>
              <ReorderableList
                items={stepItems}
                onChange={(next) => {
                  const mapped = next.map((item, idx) => form.steps.find((s) => s.id === item.id) ?? form.steps[idx]);
                  setForm({ ...form, steps: mapped.filter((s): s is SequenceStep => !!s?.commandId) });
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

            <FormSection title="Conditional Flow" description="Optional branching by condition." id="sequence-edit-conditional-flow">
              <div className="field">
                <label htmlFor="edit-conditional-flow-enabled">
                  <input
                    id="edit-conditional-flow-enabled"
                    aria-label="Enable conditional flow"
                    type="checkbox"
                    checked={conditionalEnabled}
                    onChange={(event) => {
                      setConditionalEnabled(event.target.checked);
                      if (event.target.checked) {
                        setPerStepEnabled(false);
                      }
                      if (event.target.checked && !entryStepId) {
                        setEntryStepId(commandStepIds[0] ?? '');
                      }
                      setDirty(true);
                    }}
                    disabled={submitting || loading}
                  />
                  Enable conditional flow
                </label>
              </div>

              {conditionalEnabled && (
                <>
                  <div className="field">
                    <label htmlFor="sequence-edit-entry-step">Entry Step</label>
                    <select
                      id="sequence-edit-entry-step"
                      aria-label="Entry Step"
                      value={entryStepId}
                      onChange={(event) => {
                        setEntryStepId(event.target.value);
                        setDirty(true);
                      }}
                    >
                      <option value="">Select entry step</option>
                      {commandStepIds.map((commandId) => (
                        <option key={commandId} value={commandId}>{commandLookup.get(commandId) ?? commandId}</option>
                      ))}
                    </select>
                  </div>

                  <div className="field">
                    <button
                      type="button"
                      onClick={() => {
                        const nextIndex = conditions.length + 1;
                        setConditions((prev) => [
                          ...prev,
                          {
                            stepId: `condition-${nextIndex}`,
                            sourceStepId: entryStepId || commandStepIds[0] || '',
                            trueTargetId: '',
                            falseTargetId: '',
                            expression: createDefaultConditionExpression()
                          }
                        ]);
                        setDirty(true);
                      }}
                      disabled={submitting || loading || commandStepIds.length === 0}
                    >
                      Add Condition Step
                    </button>
                  </div>

                  {conditions.map((condition, index) => (
                    <div key={`edit-${index}`} className="field">
                      <label htmlFor={`edit-condition-step-id-${index}`}>Condition Step Id</label>
                      <input
                        id={`edit-condition-step-id-${index}`}
                        aria-label="Condition Step Id"
                        value={condition.stepId}
                        onChange={(event) => {
                          setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, stepId: event.target.value } : item));
                          setDirty(true);
                        }}
                      />

                      <SequenceBranchConnector
                        options={commandStepIds.map((stepId) => ({ value: stepId, label: commandLookup.get(stepId) ?? stepId }))}
                        trueTargetId={condition.trueTargetId}
                        falseTargetId={condition.falseTargetId}
                        onTrueTargetChange={(value) => {
                          setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, trueTargetId: value } : item));
                          setDirty(true);
                        }}
                        onFalseTargetChange={(value) => {
                          setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, falseTargetId: value } : item));
                          setDirty(true);
                        }}
                      />

                      <ConditionExpressionBuilder
                        value={condition.expression}
                        onChange={(value) => {
                          setConditions((prev) => prev.map((item, itemIndex) => itemIndex === index ? { ...item, expression: value } : item));
                          setDirty(true);
                        }}
                      />
                    </div>
                  ))}
                </>
              )}
            </FormSection>

            <FormSection title="Per-Step Conditions" description="Optional conditions evaluated before each step." id="sequence-edit-per-step-conditions">
              <div className="field">
                <label htmlFor="edit-per-step-conditions-enabled">
                  <input
                    id="edit-per-step-conditions-enabled"
                    aria-label="Enable per-step conditions"
                    type="checkbox"
                    checked={perStepEnabled}
                    onChange={(event) => {
                      setPerStepEnabled(event.target.checked);
                      if (event.target.checked) {
                        setConditionalEnabled(false);
                      }
                      setDirty(true);
                    }}
                    disabled={submitting || loading}
                  />
                  Enable per-step conditions
                </label>
              </div>

              {perStepEnabled && form.steps.map((step, index) => (
                <div key={`edit-per-step-${step.id}`} className="field">
                  <label htmlFor={`edit-step-id-${step.id}`}>Step Id ({step.commandId})</label>
                  <input
                    id={`edit-step-id-${step.id}`}
                    aria-label={`Step Id (${step.commandId})`}
                    value={step.stepId}
                    onChange={(event) => {
                      setForm((prev) => ({
                        ...prev,
                        steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, stepId: event.target.value } : candidate)
                      }));
                      setDirty(true);
                    }}
                  />

                  <label htmlFor={`edit-step-condition-type-${step.id}`}>Condition Type ({step.commandId})</label>
                  <select
                    id={`edit-step-condition-type-${step.id}`}
                    aria-label={`Condition Type (${step.commandId})`}
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
                  >
                    <option value="none">None</option>
                    <option value="imageVisible">imageVisible</option>
                    <option value="commandOutcome">commandOutcome</option>
                  </select>

                  {step.conditionType === 'imageVisible' && (
                    <>
                      <label htmlFor={`edit-step-image-id-${step.id}`}>Image Id ({step.commandId})</label>
                      <input
                        id={`edit-step-image-id-${step.id}`}
                        aria-label={`Image Id (${step.commandId})`}
                        value={step.imageId}
                        onChange={(event) => {
                          setForm((prev) => ({
                            ...prev,
                            steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, imageId: event.target.value } : candidate)
                          }));
                          setDirty(true);
                        }}
                      />

                      <label htmlFor={`edit-step-min-similarity-${step.id}`}>Min Similarity ({step.commandId})</label>
                      <input
                        id={`edit-step-min-similarity-${step.id}`}
                        aria-label={`Min Similarity (${step.commandId})`}
                        value={step.minSimilarity}
                        onChange={(event) => {
                          setForm((prev) => ({
                            ...prev,
                            steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, minSimilarity: event.target.value } : candidate)
                          }));
                          setDirty(true);
                        }}
                      />
                    </>
                  )}

                  {step.conditionType === 'commandOutcome' && (
                    <>
                      <label htmlFor={`edit-step-step-ref-${step.id}`}>Step Ref ({step.commandId})</label>
                      <select
                        id={`edit-step-step-ref-${step.id}`}
                        aria-label={`Step Ref (${step.commandId})`}
                        value={step.outcomeStepRef}
                        onChange={(event) => {
                          setForm((prev) => ({
                            ...prev,
                            steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, outcomeStepRef: event.target.value } : candidate)
                          }));
                          setDirty(true);
                        }}
                      >
                        <option value="">Select prior step</option>
                        {form.steps.slice(0, index).map((candidate) => (
                          <option key={candidate.id} value={candidate.stepId}>{candidate.stepId}</option>
                        ))}
                      </select>

                      <label htmlFor={`edit-step-expected-state-${step.id}`}>Expected State ({step.commandId})</label>
                      <select
                        id={`edit-step-expected-state-${step.id}`}
                        aria-label={`Expected State (${step.commandId})`}
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
                      >
                        <option value="success">success</option>
                        <option value="failed">failed</option>
                        <option value="skipped">skipped</option>
                      </select>
                    </>
                  )}
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

