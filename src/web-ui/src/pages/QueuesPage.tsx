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
  startQueue,
  stopQueue,
} from '../services/queues';
import { listSequences, SequenceDto } from '../services/sequences';
import { QueueForm, QueueFormValue } from '../components/queues/QueueForm';
import { QueueEntryList } from '../components/queues/QueueEntryList';
import { SaveTemplateDialog } from '../components/queues/SaveTemplateDialog';
import { TemplatePickerDialog } from '../components/queues/TemplatePickerDialog';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { saveQueueTemplate, getQueueTemplate } from '../services/queueTemplates';
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
  const [saveTemplateOpen, setSaveTemplateOpen] = useState(false);
  const [loadedTemplateName, setLoadedTemplateName] = useState<string | undefined>(undefined);
  const [templatePickerOpen, setTemplatePickerOpen] = useState(false);
  const [pendingLoad, setPendingLoad] = useState<{ name: string; sequenceIds: string[] } | undefined>(undefined);

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
    setLoadedTemplateName(undefined);
    const q = await getQueue(id);
    setDetail(q);
    setForm({ name: q.name, emulatorSerial: q.emulatorSerial, cycleExecution: q.cycleExecution });
  };

  const closeForms = () => {
    setCreating(false);
    setDetail(undefined);
    setForm(emptyForm);
    setFieldErrors(undefined);
    setFormError(undefined);
    setLoadedTemplateName(undefined);
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

  const runAction = async (id: string, action: (id: string) => Promise<unknown>) => {
    try {
      await action(id);
      await refresh();
      if (detail?.id === id) await reloadDetail(id);
    } catch (err: any) {
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
    await saveQueueTemplate({ name, sequenceIds, overwrite });
    setLoadedTemplateName(name);
    setTableMessage(`Template "${name}" saved successfully.`);
  };

  const applyLoad = async (name: string, sequenceIds: string[]) => {
    if (!detail) return;
    try {
      await replaceQueueEntries(detail.id, sequenceIds);
      setLoadedTemplateName(name);
      setTemplatePickerOpen(false);
      setTableMessage(`Template "${name}" loaded.`);
      await reloadDetail(detail.id);
      await refresh();
    } catch (err: any) {
      setTemplatePickerOpen(false);
      setTableError(err instanceof ApiError ? err.message : err?.message ?? 'Failed to load template');
    }
  };

  const handleLoadTemplate = async (templateId: string) => {
    if (!detail) return;
    const tpl = await getQueueTemplate(templateId);
    const sequenceIds = tpl.entries.map((e) => e.sequenceId);
    if (detail.entries.length > 0) {
      setPendingLoad({ name: tpl.name, sequenceIds });
    } else {
      await applyLoad(tpl.name, sequenceIds);
    }
  };

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
                    <button type="button" onClick={() => void runAction(q.id, stopQueue)}>Stop</button>
                  ) : (
                    <button type="button" onClick={() => void runAction(q.id, startQueue)}>Start</button>
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
          />
          <section className="queue-templates-section" aria-label="Queue templates">
            <h4>Templates</h4>
            <div className="queue-template-actions">
              <button type="button" onClick={() => setSaveTemplateOpen(true)}>Save as template</button>
              <button
                type="button"
                onClick={() => setTemplatePickerOpen(true)}
                disabled={detail.status === 'Running'}
                title={detail.status === 'Running' ? 'Stop the queue before loading a template.' : undefined}
              >
                Load template
              </button>
            </div>
          </section>
          <QueueEntryList
            entries={detail.entries}
            sequences={sequences}
            onAdd={(sid) => void onAddEntry(sid)}
            onRemove={(eid) => void onRemoveEntry(eid)}
          />
        </section>
      )}

      <SaveTemplateDialog
        open={saveTemplateOpen}
        originName={loadedTemplateName}
        onSave={handleSaveTemplate}
        onClose={() => setSaveTemplateOpen(false)}
      />

      <TemplatePickerDialog
        open={templatePickerOpen}
        loadDisabled={detail?.status === 'Running'}
        onLoad={(id) => void handleLoadTemplate(id)}
        onClose={() => setTemplatePickerOpen(false)}
      />

      <ConfirmDeleteModal
        open={pendingLoad !== undefined}
        title="Replace queue entries"
        message="Loading this template will replace the queue's current entries."
        confirmText="Replace"
        onCancel={() => setPendingLoad(undefined)}
        onConfirm={() => {
          const p = pendingLoad;
          setPendingLoad(undefined);
          if (p) void applyLoad(p.name, p.sequenceIds);
        }}
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
