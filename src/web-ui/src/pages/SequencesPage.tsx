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
import { validateConditionalFlow } from '../lib/validation';
import { buildSequenceFlow, ConditionDraft, createDefaultConditionExpression } from '../lib/sequenceFlowGraph';
import { ConditionExpressionBuilder } from '../components/authoring/ConditionExpressionBuilder';
import { SequenceBranchConnector } from '../components/authoring/SequenceBranchConnector';
import type { FlowStep, ConditionExpression, BranchLink } from '../types/sequenceFlow';

type SequenceStep = { id: string; commandId: string };

type SequenceFormValue = {
  name: string;
  steps: SequenceStep[];
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: SequenceFormValue = { name: '', steps: [] };

const toStepEntries = (ids?: string[]): SequenceStep[] => (ids ?? []).map((cmdId) => ({ id: makeId(), commandId: cmdId }));

const toPayloadSteps = (steps: SequenceStep[]) => steps.map((s) => s.commandId);

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
    const flowSteps = isFlowStepArray(s.steps) ? s.steps : [];
    const links = Array.isArray(s.links) ? s.links : [];
    const hasConditionalFlow = !!s.entryStepId && flowSteps.length > 0 && links.length > 0;
    setEditingId(id);
    setCreating(false);
    setPendingStepId(undefined);
    setForm({ name: s.name, steps: toStepEntries(commandIds) });
    setLoadedVersion(s.version ?? 1);
    setConditionalEnabled(hasConditionalFlow);
    setEntryStepId(hasConditionalFlow ? s.entryStepId! : (commandIds[0] ?? ''));
    setConditions(hasConditionalFlow ? toConditionDrafts(flowSteps, links, commandIds[0] ?? '') : []);
    setDirty(false);
  };

  const resetForm = () => {
    setForm(emptyForm);
    setPendingStepId(undefined);
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

            if (conditionalPayload) {
              const conditionalErrors = validateConditionalFlow(conditionalPayload);
              if (conditionalErrors.length > 0) {
                setErrors({ form: conditionalErrors[0] });
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
                const next = [...form.steps, { id: makeId(), commandId: pendingStepId }];
                setForm({ ...form, steps: next });
                setPendingStepId(undefined);
                setDirty(true);
              }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
            </div>
            <ReorderableList
              items={stepItems}
              onChange={(next) => {
                const mapped = next.map((item, idx) => ({ id: item.id, commandId: form.steps.find((s) => s.id === item.id)?.commandId ?? form.steps[idx]?.commandId ?? '' }));
                setForm({ ...form, steps: mapped.filter((s) => s.commandId) });
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

              if (conditionalPayload) {
                const conditionalErrors = validateConditionalFlow(conditionalPayload);
                if (conditionalErrors.length > 0) {
                  setErrors({ form: conditionalErrors[0] });
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
                  const next = [...form.steps, { id: makeId(), commandId: pendingStepId }];
                  setForm({ ...form, steps: next });
                  setPendingStepId(undefined);
                  setDirty(true);
                }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
              </div>
              <ReorderableList
                items={stepItems}
                onChange={(next) => {
                  const mapped = next.map((item, idx) => ({ id: item.id, commandId: form.steps.find((s) => s.id === item.id)?.commandId ?? form.steps[idx]?.commandId ?? '' }));
                  setForm({ ...form, steps: mapped.filter((s) => s.commandId) });
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

const toCommandStepIds = (steps: SequenceDto['steps']): string[] => {
  if (steps.length === 0) {
    return [];
  }

  const first = steps[0];
  if (typeof first === 'string') {
    return steps as string[];
  }

  return (steps as Array<{ stepType?: string; payloadRef?: string | null }> )
    .filter((step) => step.stepType === 'command' && !!step.payloadRef)
    .map((step) => step.payloadRef as string);
};

const isFlowStepArray = (steps: SequenceDto['steps']): steps is FlowStep[] => {
  return Array.isArray(steps) && steps.every((step) => typeof step === 'object' && step !== null && 'stepId' in step);
};

const toConditionDrafts = (steps: FlowStep[], links: BranchLink[], fallbackSourceStepId: string): ConditionDraft[] => {
  return steps
    .filter((step) => step.stepType === 'condition')
    .map((step) => {
      const nextLink = links.find((link) => link.targetStepId === step.stepId && link.branchType === 'next');
      const trueLink = links.find((link) => link.sourceStepId === step.stepId && link.branchType === 'true');
      const falseLink = links.find((link) => link.sourceStepId === step.stepId && link.branchType === 'false');

      return {
        stepId: step.stepId,
        sourceStepId: nextLink?.sourceStepId ?? fallbackSourceStepId,
        trueTargetId: trueLink?.targetStepId ?? '',
        falseTargetId: falseLink?.targetStepId ?? '',
        expression: step.condition ?? (createDefaultConditionExpression() as ConditionExpression)
      };
    });
};
