import React, { useCallback, useEffect, useState } from 'react';
import {
  QueueDto,
  QueueDetailDto,
  listQueues,
  getQueue,
  createQueue,
  updateQueue,
  deleteQueue,
  addQueueEntry,
  removeQueueEntry,
  replaceQueueEntries,
  setQueueTemplateLink,
  setQueueGameLink,
  startQueue,
  stopQueue,
  liveScheduleSequence,
} from '../services/queues';
import { listSequences, SequenceDto } from '../services/sequences';
import { QueueForm, QueueFormValue } from '../components/queues/QueueForm';
import { EntrySchedule } from '../components/queues/QueueEntryList';
import { QueueSchedulingAreas } from '../components/queues/QueueSchedulingAreas';
import { SchedulingAreasState } from '../components/queues/schedulingAreas';
import { QueueLiveScheduleControl } from '../components/queues/QueueLiveScheduleControl';
import { QueueTemplateControls } from '../components/queues/QueueTemplateControls';
import { QueueGameControls } from '../components/queues/QueueGameControls';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { saveQueueTemplate, getQueueTemplate, listQueueTemplates, ScheduleType, QueueTemplateEntryDto } from '../services/queueTemplates';
import { sameSequenceOrder } from '../lib/sequenceOrder';
import { ApiError } from '../lib/api';

const emptyForm: QueueFormValue = { name: '', emulatorSerial: '', cycleExecution: false };

export const QueuesPage: React.FC = () => {
  const [queues, setQueues] = useState<QueueDto[]>([]);
  const [sequences, setSequences] = useState<SequenceDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [tableError, setTableError] = useState<string | undefined>(undefined);
  const [tableMessage, setTableMessage] = useState<string | undefined>(undefined);

  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState<QueueFormValue>(emptyForm);
  const [fieldErrors, setFieldErrors] = useState<{ name?: string; emulatorSerial?: string } | undefined>(undefined);
  const [formError, setFormError] = useState<string | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);

  // Inline save outcomes shown at the click site (replaces the page-top banner for these two saves).
  const [queueSaveResult, setQueueSaveResult] = useState<{ kind: 'success' | 'error'; message: string } | undefined>(undefined);
  const [templateSaveResult, setTemplateSaveResult] = useState<{ kind: 'success' | 'error'; message: string } | undefined>(undefined);

  const [detail, setDetail] = useState<QueueDetailDto | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [associatedTemplateName, setAssociatedTemplateName] = useState<string | undefined>(undefined);
  const [pendingLoad, setPendingLoad] = useState<{ name: string; sequenceIds: string[]; templateId: string; templateEntries?: QueueTemplateEntryDto[] } | undefined>(undefined);
  const [pendingReload, setPendingReload] = useState<{ name: string; sequenceIds: string[]; templateId: string; templateEntries?: QueueTemplateEntryDto[] } | undefined>(undefined);

  // Per-entry schedule state (UI-local; not stored in the runtime API).
  // Keys are entryIds from the runtime queue entries.
  const [entrySchedule, setEntrySchedule] = useState<Record<string, EntrySchedule>>({});

  // Which running queue (if any) currently has its live-schedule control expanded.
  const [liveScheduleFor, setLiveScheduleFor] = useState<string | undefined>(undefined);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const data = await listQueues();
      setQueues(data);
      setTableError(undefined);
    } catch (err: any) {
      setQueues([]);
      setTableError(err?.message ?? 'Failed to load queues');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    listSequences().then(setSequences).catch(() => setSequences([]));
  }, [refresh]);

  const reloadDetail = useCallback(async (id: string) => {
    try {
      setDetail(await getQueue(id));
    } catch (err: any) {
      setFormError(err?.message ?? 'Failed to load queue');
    }
  }, []);

  const openCreate = () => {
    setCreating(true);
    setDetail(undefined);
    setForm(emptyForm);
    setFieldErrors(undefined);
    setFormError(undefined);
    setQueueSaveResult(undefined);
    setTemplateSaveResult(undefined);
  };

  const openEdit = async (id: string) => {
    setCreating(false);
    setFieldErrors(undefined);
    setFormError(undefined);
    setQueueSaveResult(undefined);
    setTemplateSaveResult(undefined);
    const q = await getQueue(id);
    setDetail(q);
    setAssociatedTemplateName(q.linkedTemplateName ?? undefined);
    setForm({ name: q.name, emulatorSerial: q.emulatorSerial, cycleExecution: q.cycleExecution });

    // Restore per-entry schedule state from the linked template so that the editor
    // reflects previously saved schedule types and saving doesn't overwrite them with OncePerRun.
    if (q.linkedTemplateId && q.entries.length > 0) {
      try {
        const tpl = await getQueueTemplate(q.linkedTemplateId);
        setEntrySchedule(buildScheduleFromTemplateEntries(q.entries.map((e) => e.entryId), tpl.entries));
      } catch {
        // Template may have been deleted; leave entrySchedule empty (defaults to OncePerRun).
      }
    }
  };

  const closeForms = () => {
    setCreating(false);
    setDetail(undefined);
    setForm(emptyForm);
    setFieldErrors(undefined);
    setFormError(undefined);
    setQueueSaveResult(undefined);
    setTemplateSaveResult(undefined);
    setAssociatedTemplateName(undefined);
    setEntrySchedule({});
  };

  const buildScheduleFromTemplateEntries = (runtimeEntryIds: string[], tplEntries: QueueTemplateEntryDto[]): Record<string, EntrySchedule> => {
    const schedule: Record<string, EntrySchedule> = {};
    runtimeEntryIds.forEach((entryId, i) => {
      const tplEntry = tplEntries[i];
      if (tplEntry) {
        schedule[entryId] = {
          scheduleType: tplEntry.scheduleType ?? 'OncePerRun',
          timerTimeOfDay: tplEntry.timerTimeOfDay ?? '',
          timerMode: tplEntry.timerRelativeOffset ? 'relative' : 'timeOfDay',
          timerRelativeOffset: tplEntry.timerRelativeOffset ?? '',
        };
      } else {
        schedule[entryId] = { scheduleType: 'OncePerRun', timerTimeOfDay: '' };
      }
    });
    return schedule;
  };

  const validate = (): boolean => {
    const errs: { name?: string; emulatorSerial?: string } = {};
    if (!form.name.trim()) errs.name = 'Name is required';
    if (creating && !form.emulatorSerial.trim()) errs.emulatorSerial = 'Emulator is required';
    setFieldErrors(Object.keys(errs).length ? errs : undefined);
    return Object.keys(errs).length === 0;
  };

  const submitForm = async () => {
    if (!validate()) return;
    setSubmitting(true);
    setFormError(undefined);
    setQueueSaveResult(undefined);
    try {
      if (creating) {
        await createQueue({ name: form.name.trim(), emulatorSerial: form.emulatorSerial.trim(), cycleExecution: form.cycleExecution });
        setQueueSaveResult({ kind: 'success', message: 'Queue created successfully.' });
      } else if (detail) {
        await updateQueue(detail.id, { name: form.name.trim(), cycleExecution: form.cycleExecution });
        setQueueSaveResult({ kind: 'success', message: 'Queue updated successfully.' });
      }
      // Keep the form open so the confirmation is visible at the Save action (FR-006); just
      // refresh the overview so the row reflects the saved name/cycle.
      await refresh();
    } catch (err: any) {
      setQueueSaveResult({ kind: 'error', message: err?.message ?? 'Failed to save queue' });
    } finally {
      setSubmitting(false);
    }
  };

  const runAction = async (id: string, action: (id: string) => Promise<unknown>, successMessage?: string) => {
    try {
      setTableError(undefined);
      await action(id);
      await refresh();
      if (detail?.id === id) await reloadDetail(id);
      if (successMessage) setTableMessage(successMessage);
    } catch (err: any) {
      // Start/stop request errors (e.g. 409 already_running) surface here; the run's own
      // pass/fail outcome is recorded in the Execution Logs.
      setTableError(err?.message ?? 'Action failed');
    }
  };

  const onAddEntry = async (sequenceId: string) => {
    if (!detail) return;
    const newEntry = await addQueueEntry(detail.id, sequenceId);
    setEntrySchedule((prev) => ({ ...prev, [newEntry.entryId]: { scheduleType: 'OncePerRun' as ScheduleType, timerTimeOfDay: '' } }));
    await reloadDetail(detail.id);
    await refresh();
  };

  const onRemoveEntry = async (entryId: string) => {
    if (!detail) return;
    await removeQueueEntry(detail.id, entryId);
    setEntrySchedule((prev) => {
      const next = { ...prev };
      delete next[entryId];
      return next;
    });
    await reloadDetail(detail.id);
    await refresh();
  };

  // Reflect a drag reorder/reassign immediately: reorder the working entries into the new
  // canonical order and apply the new schedule map (cross-area reassignment persists on save).
  const onReorderAndReassign = (next: SchedulingAreasState) => {
    setEntrySchedule(next.schedule);
    setDetail((prev) => {
      if (!prev) return prev;
      const byId = new Map(prev.entries.map((e) => [e.entryId, e]));
      const reordered = next.orderedEntryIds
        .map((id) => byId.get(id))
        .filter((e): e is QueueDetailDto['entries'][number] => Boolean(e));
      return { ...prev, entries: reordered };
    });
  };

  const handleSaveTemplate = async (name: string, overwrite: boolean) => {
    if (!detail) return;
    // Order-aware save: persist the current linear order to the runtime queue first, then build
    // the template entries in that exact order so the positional reload restore stays correct.
    const orderedSequenceIds = detail.entries.map((e) => e.sequenceId);
    const orderedSchedules = detail.entries.map(
      (e) => entrySchedule[e.entryId] ?? { scheduleType: 'OncePerRun' as ScheduleType, timerTimeOfDay: '' }
    );
    await replaceQueueEntries(detail.id, orderedSequenceIds);
    const refreshed = await getQueue(detail.id);
    // replaceQueueEntries regenerates entryIds: re-key the schedule onto the new entries by position.
    const rekeyed: Record<string, EntrySchedule> = {};
    refreshed.entries.forEach((e, i) => {
      rekeyed[e.entryId] = orderedSchedules[i] ?? { scheduleType: 'OncePerRun', timerTimeOfDay: '' };
    });
    const entries = refreshed.entries.map((e, i) => {
      const sched = orderedSchedules[i] ?? { scheduleType: 'OncePerRun' as ScheduleType, timerTimeOfDay: '' };
      const timerMode = sched.timerMode ?? (sched.timerRelativeOffset ? 'relative' : 'timeOfDay');
      const isRelative = sched.scheduleType === 'Timer' && timerMode === 'relative';
      return {
        sequenceId: e.sequenceId,
        scheduleType: sched.scheduleType,
        ...(sched.scheduleType === 'Timer' && !isRelative && sched.timerTimeOfDay ? { timerTimeOfDay: sched.timerTimeOfDay } : {}),
        ...(isRelative && sched.timerRelativeOffset ? { timerRelativeOffset: sched.timerRelativeOffset } : {}),
      };
    });
    const saved = await saveQueueTemplate({ name, entries, overwrite });
    await setQueueTemplateLink(detail.id, saved.id);
    setDetail(refreshed);
    setEntrySchedule(rekeyed);
    setAssociatedTemplateName(name);
    setTemplateSaveResult({ kind: 'success', message: `Template "${name}" saved successfully.` });
  };

  const applyLoad = async (name: string, sequenceIds: string[], templateId: string, tplEntries?: QueueTemplateEntryDto[]) => {
    if (!detail) return;
    try {
      await replaceQueueEntries(detail.id, sequenceIds);
      await setQueueTemplateLink(detail.id, templateId);
      setAssociatedTemplateName(name);
      setTableMessage(`Template "${name}" loaded.`);
      const refreshed = await getQueue(detail.id);
      setDetail(refreshed);
      if (tplEntries) {
        setEntrySchedule(buildScheduleFromTemplateEntries(refreshed.entries.map((e) => e.entryId), tplEntries));
      }
      await refresh();
    } catch (err: any) {
      setTableError(err instanceof ApiError ? err.message : err?.message ?? 'Failed to load template');
    }
  };

  const handleLoadTemplate = async (templateId: string) => {
    if (!detail) return;
    const tpl = await getQueueTemplate(templateId);
    const sequenceIds = tpl.entries.map((e) => e.sequenceId);
    if (detail.entries.length > 0) {
      setPendingLoad({ name: tpl.name, sequenceIds, templateId: tpl.id, templateEntries: tpl.entries });
    } else {
      await applyLoad(tpl.name, sequenceIds, tpl.id, tpl.entries);
    }
  };

  const handleReload = async () => {
    if (!detail || !associatedTemplateName || detail.status === 'Running') return;
    try {
      const templates = await listQueueTemplates();
      const match = templates.find(
        (t) => t.name.toLowerCase() === associatedTemplateName.toLowerCase()
      );
      if (!match) {
        setTableError(`Template "${associatedTemplateName}" is no longer available.`);
        return;
      }
      const tpl = await getQueueTemplate(match.id);
      const templateSeqIds = tpl.entries.map((e) => e.sequenceId);
      const currentSeqIds = detail.entries.map((e) => e.sequenceId);
      if (sameSequenceOrder(currentSeqIds, templateSeqIds)) return; // nothing to discard, no prompt
      if (detail.entries.length > 0) {
        setPendingReload({ name: tpl.name, sequenceIds: templateSeqIds, templateId: match.id, templateEntries: tpl.entries });
      } else {
        await applyLoad(tpl.name, templateSeqIds, match.id, tpl.entries);
      }
    } catch (err: any) {
      setTableError(err instanceof ApiError ? err.message : err?.message ?? 'Failed to reload template');
    }
  };

  const handleLinkGame = async (gameId: string) => {
    if (!detail) return;
    try {
      const updated = await setQueueGameLink(detail.id, gameId);
      setDetail(updated);
      await refresh();
    } catch (err: any) {
      setTableError(err?.message ?? 'Failed to link game');
    }
  };

  const handleUnlinkGame = async () => {
    if (!detail) return;
    try {
      const updated = await setQueueGameLink(detail.id, null);
      setDetail(updated);
      await refresh();
    } catch (err: any) {
      setTableError(err?.message ?? 'Failed to unlink game');
    }
  };

  // Replace/reload confirmation shown inline within the template controls (where the picker was).
  const templateConfirm = pendingLoad
    ? {
        title: 'Replace queue entries',
        message: "Loading this template will replace the queue's current entries.",
        confirmText: 'Replace',
        onCancel: () => setPendingLoad(undefined),
        onConfirm: () => {
          const p = pendingLoad;
          setPendingLoad(undefined);
          if (p) void applyLoad(p.name, p.sequenceIds, p.templateId, p.templateEntries);
        },
      }
    : pendingReload
      ? {
          title: 'Reload template',
          message: "Reloading will replace the queue's current entries with the template's.",
          confirmText: 'Reload',
          onCancel: () => setPendingReload(undefined),
          onConfirm: () => {
            const p = pendingReload;
            setPendingReload(undefined);
            if (p) void applyLoad(p.name, p.sequenceIds, p.templateId, p.templateEntries);
          },
        }
      : undefined;

  return (
    <section>
      <h2>Queues</h2>
      {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
      {tableError && <div className="form-error" role="alert">{tableError}</div>}
      <div className="actions-header">
        <button onClick={openCreate}>Create Queue</button>
      </div>
      <table className="queues-table" aria-label="Queues table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Emulator</th>
            <th>Cycle</th>
            <th>Status</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {loading && <tr><td colSpan={5}>Loading…</td></tr>}
          {!loading && queues.length === 0 && <tr><td colSpan={5}>No queues found.</td></tr>}
          {!loading && queues.map((q) => {
            const running = q.status === 'Running';
            return (
              <React.Fragment key={q.id}>
                <tr className="queues-row">
                  <td>
                    <button type="button" className="link-button" onClick={() => void openEdit(q.id)}>{q.name}</button>
                  </td>
                  <td>{q.emulatorSerial}</td>
                  <td>{q.cycleExecution ? 'On' : 'Off'}</td>
                  <td>
                    <span className={running ? 'status-chip status-running' : 'status-chip status-stopped'} data-testid="queue-status">
                      {q.status}
                    </span>
                  </td>
                  <td>
                    {running ? (
                      <button type="button" onClick={() => void runAction(q.id, stopQueue, 'Queue stopped.')}>Stop</button>
                    ) : (
                      <button type="button" onClick={() => void runAction(q.id, startQueue, 'Queue started — see Execution Logs for progress and the run outcome.')}>Start</button>
                    )}
                    {running && (
                      <button
                        type="button"
                        aria-label={`Schedule a sequence for ${q.name}`}
                        onClick={() => setLiveScheduleFor((cur) => (cur === q.id ? undefined : q.id))}
                      >
                        Schedule
                      </button>
                    )}
                    <button type="button" onClick={() => void openEdit(q.id)} disabled={running}>Edit</button>
                    <button
                      type="button"
                      className="btn btn-danger"
                      disabled={running}
                      onClick={async () => { await openEdit(q.id); setDeleteOpen(true); }}
                    >
                      Delete
                    </button>
                  </td>
                </tr>
                {running && liveScheduleFor === q.id && (
                  <tr className="queues-live-schedule-row">
                    <td colSpan={5}>
                      <QueueLiveScheduleControl
                        sequences={sequences}
                        onSchedule={(sequenceId, offset) => liveScheduleSequence(q.id, sequenceId, offset)}
                      />
                    </td>
                  </tr>
                )}
              </React.Fragment>
            );
          })}
        </tbody>
      </table>

      {creating && (
        <section>
          <h3>Create Queue</h3>
          <QueueForm
            mode="create"
            value={form}
            onChange={setForm}
            onSubmit={() => void submitForm()}
            onCancel={closeForms}
            submitting={submitting}
            formError={formError}
            saveResult={queueSaveResult}
            fieldErrors={fieldErrors}
          />
        </section>
      )}

      {detail && !creating && (
        <section>
          <h3>Edit Queue</h3>
          <QueueForm
            mode="edit"
            value={form}
            onChange={setForm}
            onSubmit={() => void submitForm()}
            onCancel={closeForms}
            submitting={submitting}
            formError={formError}
            saveResult={queueSaveResult}
            fieldErrors={fieldErrors}
            templateControls={
              <QueueTemplateControls
                associatedTemplateName={associatedTemplateName}
                status={detail.status}
                onSaveTemplate={handleSaveTemplate}
                onLoadTemplate={(id) => void handleLoadTemplate(id)}
                onReload={() => void handleReload()}
                pendingConfirm={templateConfirm}
                saveResult={templateSaveResult}
              >
                <QueueSchedulingAreas
                  entries={detail.entries}
                  sequences={sequences}
                  onAdd={(sid) => void onAddEntry(sid)}
                  onRemove={(eid) => void onRemoveEntry(eid)}
                  entrySchedule={entrySchedule}
                  onReorderAndReassign={onReorderAndReassign}
                  onTimerTimeChange={(eid, t) => setEntrySchedule((prev) => ({ ...prev, [eid]: { ...prev[eid] ?? { scheduleType: 'Timer', timerTimeOfDay: '' }, timerTimeOfDay: t } }))}
                  onTimerModeChange={(eid, mode) => setEntrySchedule((prev) => ({ ...prev, [eid]: { ...prev[eid] ?? { scheduleType: 'Timer', timerTimeOfDay: '' }, timerMode: mode } }))}
                  onTimerRelativeOffsetChange={(eid, offset) => setEntrySchedule((prev) => ({ ...prev, [eid]: { ...prev[eid] ?? { scheduleType: 'Timer', timerTimeOfDay: '' }, timerMode: 'relative', timerRelativeOffset: offset } }))}
                  disabled={detail.status === 'Running'}
                />
              </QueueTemplateControls>
            }
            gameControls={
              <QueueGameControls
                linkedGameId={detail.linkedGameId}
                linkedGameName={detail.linkedGameName}
                status={detail.status}
                onLink={(gameId) => void handleLinkGame(gameId)}
                onUnlink={() => void handleUnlinkGame()}
              />
            }
          />
        </section>
      )}

      <ConfirmDeleteModal
        open={deleteOpen}
        itemName={form.name}
        onCancel={() => setDeleteOpen(false)}
        onConfirm={async () => {
          if (!detail) return;
          try {
            await deleteQueue(detail.id);
            setDeleteOpen(false);
            closeForms();
            setTableMessage('Queue deleted successfully.');
            await refresh();
          } catch (err: any) {
            setDeleteOpen(false);
            if (err instanceof ApiError && err.status === 409) {
              setTableError(err.message || 'Stop the queue before deleting.');
            } else {
              setTableError(err?.message ?? 'Failed to delete queue');
            }
          }
        }}
      />
    </section>
  );
};
