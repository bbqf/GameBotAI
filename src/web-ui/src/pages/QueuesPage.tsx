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
} from '../services/queues';
import { listSequences, SequenceDto } from '../services/sequences';
import { QueueForm, QueueFormValue } from '../components/queues/QueueForm';
import { QueueEntryList } from '../components/queues/QueueEntryList';
import { QueueTemplateControls } from '../components/queues/QueueTemplateControls';
import { QueueGameControls } from '../components/queues/QueueGameControls';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { saveQueueTemplate, getQueueTemplate, listQueueTemplates } from '../services/queueTemplates';
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

  const [detail, setDetail] = useState<QueueDetailDto | undefined>(undefined);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [associatedTemplateName, setAssociatedTemplateName] = useState<string | undefined>(undefined);
  const [pendingLoad, setPendingLoad] = useState<{ name: string; sequenceIds: string[]; templateId: string } | undefined>(undefined);
  const [pendingReload, setPendingReload] = useState<{ name: string; sequenceIds: string[]; templateId: string } | undefined>(undefined);

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
  };

  const openEdit = async (id: string) => {
    setCreating(false);
    setFieldErrors(undefined);
    setFormError(undefined);
    const q = await getQueue(id);
    setDetail(q);
    setAssociatedTemplateName(q.linkedTemplateName ?? undefined);
    setForm({ name: q.name, emulatorSerial: q.emulatorSerial, cycleExecution: q.cycleExecution });
  };

  const closeForms = () => {
    setCreating(false);
    setDetail(undefined);
    setForm(emptyForm);
    setFieldErrors(undefined);
    setFormError(undefined);
    setAssociatedTemplateName(undefined);
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
    try {
      if (creating) {
        await createQueue({ name: form.name.trim(), emulatorSerial: form.emulatorSerial.trim(), cycleExecution: form.cycleExecution });
        setTableMessage('Queue created successfully.');
      } else if (detail) {
        await updateQueue(detail.id, { name: form.name.trim(), cycleExecution: form.cycleExecution });
        setTableMessage('Queue updated successfully.');
      }
      closeForms();
      await refresh();
    } catch (err: any) {
      setFormError(err?.message ?? 'Failed to save queue');
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
    await addQueueEntry(detail.id, sequenceId);
    await reloadDetail(detail.id);
    await refresh();
  };

  const onRemoveEntry = async (entryId: string) => {
    if (!detail) return;
    await removeQueueEntry(detail.id, entryId);
    await reloadDetail(detail.id);
    await refresh();
  };

  const handleSaveTemplate = async (name: string, overwrite: boolean) => {
    if (!detail) return;
    const sequenceIds = detail.entries.map((e) => e.sequenceId);
    const saved = await saveQueueTemplate({ name, sequenceIds, overwrite });
    await setQueueTemplateLink(detail.id, saved.id);
    setAssociatedTemplateName(name);
    setTableMessage(`Template "${name}" saved successfully.`);
  };

  const applyLoad = async (name: string, sequenceIds: string[], templateId: string) => {
    if (!detail) return;
    try {
      await replaceQueueEntries(detail.id, sequenceIds);
      await setQueueTemplateLink(detail.id, templateId);
      setAssociatedTemplateName(name);
      setTableMessage(`Template "${name}" loaded.`);
      await reloadDetail(detail.id);
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
      setPendingLoad({ name: tpl.name, sequenceIds, templateId: tpl.id });
    } else {
      await applyLoad(tpl.name, sequenceIds, tpl.id);
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
        setPendingReload({ name: tpl.name, sequenceIds: templateSeqIds, templateId: match.id });
      } else {
        await applyLoad(tpl.name, templateSeqIds, match.id);
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
          if (p) void applyLoad(p.name, p.sequenceIds, p.templateId);
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
            if (p) void applyLoad(p.name, p.sequenceIds, p.templateId);
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
            <th>Sequences</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {loading && <tr><td colSpan={6}>Loading…</td></tr>}
          {!loading && queues.length === 0 && <tr><td colSpan={6}>No queues found.</td></tr>}
          {!loading && queues.map((q) => {
            const running = q.status === 'Running';
            return (
              <tr key={q.id} className="queues-row">
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
                <td>{q.entryCount}</td>
                <td>
                  {running ? (
                    <button type="button" onClick={() => void runAction(q.id, stopQueue, 'Queue stopped.')}>Stop</button>
                  ) : (
                    <button type="button" onClick={() => void runAction(q.id, startQueue, 'Queue started — see Execution Logs for progress and the run outcome.')}>Start</button>
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
            fieldErrors={fieldErrors}
            templateControls={
              <QueueTemplateControls
                associatedTemplateName={associatedTemplateName}
                status={detail.status}
                onSaveTemplate={handleSaveTemplate}
                onLoadTemplate={(id) => void handleLoadTemplate(id)}
                onReload={() => void handleReload()}
                pendingConfirm={templateConfirm}
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
            entries={
              <QueueEntryList
                entries={detail.entries}
                sequences={sequences}
                onAdd={(sid) => void onAddEntry(sid)}
                onRemove={(eid) => void onRemoveEntry(eid)}
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
