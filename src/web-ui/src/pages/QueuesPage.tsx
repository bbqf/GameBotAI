import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  QueueDto,
  QueueDetailDto,
  QueueMonitorDto,
  getQueueMonitor,
  listQueues,
  getQueue,
  createQueue,
  updateQueue,
  deleteQueue,
  duplicateQueue,
  addQueueEntry,
  removeQueueEntry,
  replaceQueueEntries,
  setQueueTemplateLink,
  setQueueGameLink,
  startQueue,
  stopQueue,
} from '../services/queues';
import { listSequences, SequenceDto } from '../services/sequences';
import { QueueForm, QueueFormValue } from '../components/queues/QueueForm';
import { QueueMonitor } from '../components/queues/QueueMonitor';
import { QueueRowActions } from '../components/queues/QueueRowActions';
import { EntrySchedule } from '../components/queues/QueueEntryList';
import { QueueSchedulingAreas } from '../components/queues/QueueSchedulingAreas';
import type { ParameterBinding } from '../components/parameters/types';
import { SchedulingAreasState } from '../components/queues/schedulingAreas';
import { QueueTemplateControls } from '../components/queues/QueueTemplateControls';
import { RunningQueueEditor } from '../components/queues/RunningQueueEditor';
import { RunningQueueControls } from '../components/queues/RunningQueueControls';
import { buildTemplateSaveEntries, scheduleFromTemplateEntry } from '../components/queues/templateEntryEditing';
import { QueueGameControls } from '../components/queues/QueueGameControls';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { DuplicateQueueModal, DuplicateQueueEmulator } from '../components/queues/DuplicateQueueModal';
import { saveQueueTemplate, getQueueTemplate, listQueueTemplates, ScheduleType, QueueTemplateEntryDto } from '../services/queueTemplates';
import { sameSequenceOrder } from '../lib/sequenceOrder';
import { ApiError } from '../lib/api';
import { useResetSignal } from '../hooks/useResetSignal';
import { usePolling } from '../hooks/usePolling';
import { buildQueueActivity } from '../components/queues/queueActivity';

type QueuesPageProps = {
  navResetSignal?: number;
};

/** How often the open Edit page checks whether its queue has started (feature 173). */
const START_CHECK_INTERVAL_MS = 5000;

/** How often the list reads the queues and the monitor of each running queue (feature 173). The phone list uses the same time. */
const LIST_POLL_INTERVAL_MS = 5000;

const emptyForm: QueueFormValue = { name: '', emulatorSerial: '', cycleExecution: false, pauseWhenIdle: false, idleThresholdSeconds: 30, emulatorInstanceName: '', emulatorInstanceIndex: null, resumeOnServiceStart: false };

export const QueuesPage: React.FC<QueuesPageProps> = ({ navResetSignal }) => {
  const [queues, setQueues] = useState<QueueDto[]>([]);
  // The monitor of each running queue, for the Activity column. A queue with no entry has no data (not loaded, or the call failed).
  const [monitors, setMonitors] = useState<Record<string, QueueMonitorDto>>({});
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
  const [duplicateSource, setDuplicateSource] = useState<QueueDto | undefined>(undefined);
  const [duplicateError, setDuplicateError] = useState<string | undefined>(undefined);
  const [associatedTemplateName, setAssociatedTemplateName] = useState<string | undefined>(undefined);
  const [pendingLoad, setPendingLoad] = useState<{ name: string; sequenceIds: string[]; templateId: string; templateEntries?: QueueTemplateEntryDto[] } | undefined>(undefined);
  const [pendingReload, setPendingReload] = useState<{ name: string; sequenceIds: string[]; templateId: string; templateEntries?: QueueTemplateEntryDto[] } | undefined>(undefined);

  // Per-entry schedule state (UI-local; not stored in the runtime API).
  // Keys are entryIds from the runtime queue entries.
  const [entrySchedule, setEntrySchedule] = useState<Record<string, EntrySchedule>>({});

  // True when the queue started while the operator edited it. The running editor then keeps the unsaved edits (feature 173).
  const [startedDuringEdit, setStartedDuringEdit] = useState(false);
  // Counts the template saves of the running editor, so the notice of waiting changes reads again.
  const [templateSavedTick, setTemplateSavedTick] = useState(0);
  const detailRef = useRef<QueueDetailDto | undefined>(undefined);
  detailRef.current = detail;

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

  // A stopped queue can start while its Edit page is open. Only the status changes: the form and the working entries stay.
  // The end of a run is shown by the monitor, which offers the way back to the editor.
  const openId = detail?.id;
  useEffect(() => {
    if (!openId || creating) return;
    const timer = setInterval(() => {
      void getQueue(openId)
        .then((fresh) => {
          const current = detailRef.current;
          if (!fresh || !current || current.id !== openId) return;
          if (current.status === 'Stopped' && fresh.status === 'Running') {
            setStartedDuringEdit(true);
            setDetail({ ...current, status: 'Running' });
          }
        })
        .catch(() => undefined);
    }, START_CHECK_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [openId, creating]);

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
    setStartedDuringEdit(false);
    const q = await getQueue(id);
    setDetail(q);
    setAssociatedTemplateName(q.linkedTemplateName ?? undefined);
    setForm({ name: q.name, emulatorSerial: q.emulatorSerial, cycleExecution: q.cycleExecution, pauseWhenIdle: q.pauseWhenIdle, idleThresholdSeconds: q.idleThresholdSeconds, emulatorInstanceName: q.emulatorInstanceName ?? '', emulatorInstanceIndex: q.emulatorInstanceIndex ?? null, resumeOnServiceStart: q.resumeOnServiceStart ?? false });

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
    setStartedDuringEdit(false);
  };

  const buildScheduleFromTemplateEntries = (runtimeEntryIds: string[], tplEntries: QueueTemplateEntryDto[]): Record<string, EntrySchedule> => {
    const schedule: Record<string, EntrySchedule> = {};
    runtimeEntryIds.forEach((entryId, i) => {
      const tplEntry = tplEntries[i];
      if (tplEntry) {
        schedule[entryId] = scheduleFromTemplateEntry(tplEntry);
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
        await createQueue({ name: form.name.trim(), emulatorSerial: form.emulatorSerial.trim(), cycleExecution: form.cycleExecution, pauseWhenIdle: form.pauseWhenIdle, idleThresholdSeconds: form.idleThresholdSeconds, emulatorInstanceName: form.emulatorInstanceName.trim() || null, emulatorInstanceIndex: form.emulatorInstanceIndex, resumeOnServiceStart: form.resumeOnServiceStart });
        setQueueSaveResult({ kind: 'success', message: 'Queue created successfully.' });
      } else if (detail) {
        await updateQueue(detail.id, { name: form.name.trim(), cycleExecution: form.cycleExecution, pauseWhenIdle: form.pauseWhenIdle, idleThresholdSeconds: form.idleThresholdSeconds, emulatorInstanceName: form.emulatorInstanceName.trim() || null, emulatorInstanceIndex: form.emulatorInstanceIndex, resumeOnServiceStart: form.resumeOnServiceStart });
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

  const handleDuplicate = async (name: string, emulator: DuplicateQueueEmulator) => {
    if (!duplicateSource) return;
    try {
      await duplicateQueue(duplicateSource.id, { name, ...emulator });
      setDuplicateSource(undefined);
      setDuplicateError(undefined);
      setTableMessage(`Queue "${name}" duplicated successfully.`);
      await refresh();
    } catch (err: any) {
      // Keep the dialog open on rejection (e.g. unchanged name) so the user can correct it
      // without losing their place, instead of surfacing this at the page-level tableError.
      setDuplicateError(err instanceof ApiError ? err.message : err?.message ?? 'Failed to duplicate queue');
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

  /**
   * Records the values one entry supplies (feature 078). Held in the working schedule map and
   * persisted by the next template save, matching how the schedule fields already behave.
   */
  const onParameterValuesChange = (entryId: string, values: ParameterBinding[]) => {
    setEntrySchedule((prev) => ({
      ...prev,
      [entryId]: { ...(prev[entryId] ?? { scheduleType: 'OncePerRun', timerTimeOfDay: '' }), parameterValues: values },
    }));
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
    const entries = buildTemplateSaveEntries(
      refreshed.entries.map((e, i) => ({ sequenceId: e.sequenceId, schedule: orderedSchedules[i] }))
    );
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

  const panelOpen = creating || Boolean(detail);

  // Reads the list again (status, pause state) and the monitor of each running queue. A failed call keeps the old data.
  const pollQueues = useCallback(async () => {
    try {
      const data = await listQueues();
      if (!Array.isArray(data)) return;
      setQueues(data);
      const running = data.filter((q) => q.status === 'Running');
      const results = await Promise.all(
        running.map(async (q) => {
          try {
            return [q.id, await getQueueMonitor(q.id)] as const;
          } catch {
            return [q.id, null] as const;
          }
        })
      );
      const next: Record<string, QueueMonitorDto> = {};
      for (const [id, monitor] of results) if (monitor) next[id] = monitor;
      setMonitors(next);
    } catch {
      // Keep the rows on screen. The next poll tries again.
    }
  }, []);

  usePolling(() => { void pollQueues(); }, {
    intervalMs: LIST_POLL_INTERVAL_MS,
    enabled: !panelOpen && queues.some((q) => q.status === 'Running'),
  });

  useResetSignal(navResetSignal, () => { if (panelOpen) closeForms(); });

  return (
    <section>
      <h2>Queues</h2>
      {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
      {tableError && <div className="form-error" role="alert">{tableError}</div>}
      {!panelOpen && (
      <>
      <div className="actions-header">
        <button onClick={openCreate}>Create Queue</button>
      </div>
      <table className="queues-table" aria-label="Queues table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Cycle</th>
            <th>Status</th>
            <th>Activity</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {loading && <tr><td colSpan={5}>Loading…</td></tr>}
          {!loading && queues.length === 0 && <tr><td colSpan={5}>No queues found.</td></tr>}
          {!loading && queues.map((q) => {
            const running = q.status === 'Running';
            return (
              <tr key={q.id} className="queues-row">
                <td>
                  <button type="button" className="link-button" onClick={() => void openEdit(q.id)}>{q.name}</button>
                </td>
                <td>{q.cycleExecution ? 'On' : 'Off'}</td>
                <td>
                  <span className={running ? 'status-chip status-running' : 'status-chip status-stopped'} data-testid="queue-status">
                    {q.status}
                  </span>
                </td>
                <td data-testid="queue-activity">{buildQueueActivity(q, monitors[q.id])}</td>
                <td>
                  <QueueRowActions
                    queue={q}
                    onStart={(id) => void runAction(id, startQueue, 'Queue started — see Execution Logs for progress and the run outcome.')}
                    onStop={(id) => void runAction(id, stopQueue, 'Queue stopped.')}
                    onEdit={(id) => void openEdit(id)}
                    onDuplicate={() => { setDuplicateError(undefined); setDuplicateSource(q); }}
                    onDelete={async (id) => { await openEdit(id); setDeleteOpen(true); }}
                    onChanged={() => void pollQueues()}
                    onError={setTableError}
                  />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      </>
      )}

      {panelOpen && (
        <div className="actions-header">
          <button type="button" onClick={closeForms}>← Back to list</button>
        </div>
      )}

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

      {detail && !creating && detail.status === 'Running' && (
        <section>
          <h3>Edit Queue</h3>
          <RunningQueueControls
            queueId={detail.id}
            hasTemplate={Boolean(detail.linkedTemplateId)}
            refreshKey={templateSavedTick}
            onChanged={() => void refresh()}
          />
          <h4>Monitor: {detail.name}</h4>
          <QueueMonitor
            queueId={detail.id}
            onReturnToEditor={async () => {
              // The run has ended (or the operator chose to leave the monitor): reload the queue so
              // the panel flips back to the editor and the overview row reflects the stopped status.
              await refresh();
              await openEdit(detail.id);
            }}
          />
          <QueueForm
            mode="edit"
            running
            value={form}
            onChange={setForm}
            onSubmit={() => void submitForm()}
            onCancel={closeForms}
            submitting={submitting}
            formError={formError}
            saveResult={queueSaveResult}
            fieldErrors={fieldErrors}
            templateControls={
              <RunningQueueEditor
                key={`${detail.id}-${startedDuringEdit ? 'carried' : 'template'}`}
                queue={detail}
                sequences={sequences}
                carried={startedDuringEdit ? { entries: detail.entries, schedule: entrySchedule } : undefined}
                onTemplateSaved={() => setTemplateSavedTick((tick) => tick + 1)}
                onLinked={() => void reloadDetail(detail.id).then(refresh)}
              />
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

      {detail && !creating && detail.status !== 'Running' && (
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
                associatedTemplateId={detail.linkedTemplateId ?? undefined}
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
                  onToggleEnabled={(eid, en) => setEntrySchedule((prev) => ({ ...prev, [eid]: { ...prev[eid] ?? { scheduleType: 'OncePerRun', timerTimeOfDay: '' }, enabled: en } }))}
                  onParameterValuesChange={onParameterValuesChange}
                  // No `disabled` prop: this whole section is already gated on
                  // `detail.status !== 'Running'` above, so a `status === 'Running'` test here was
                  // provably always false (TS2367) and disabled nothing.
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

      <DuplicateQueueModal
        open={Boolean(duplicateSource)}
        sourceName={duplicateSource?.name ?? ''}
        suggestedName={`${duplicateSource?.name ?? ''} (copy)`}
        sourceEmulator={{
          emulatorSerial: duplicateSource?.emulatorSerial ?? '',
          emulatorInstanceName: duplicateSource?.emulatorInstanceName ?? null,
          emulatorInstanceIndex: duplicateSource?.emulatorInstanceIndex ?? null,
        }}
        error={duplicateError}
        onCancel={() => { setDuplicateSource(undefined); setDuplicateError(undefined); }}
        onConfirm={(name, emulator) => void handleDuplicate(name, emulator)}
      />

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
