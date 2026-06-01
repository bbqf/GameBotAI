import React, { useEffect, useState } from 'react';
import { QueueTemplateSummary, listQueueTemplates, deleteQueueTemplate } from '../../services/queueTemplates';
import { ConfirmDeleteModal } from '../ConfirmDeleteModal';

type TemplatePickerDialogProps = {
  open: boolean;
  onLoad: (templateId: string) => void;
  onClose: () => void;
  /** Disables the Load action (e.g. the queue is running). */
  loadDisabled?: boolean;
};

export const TemplatePickerDialog: React.FC<TemplatePickerDialogProps> = ({ open, onLoad, onClose, loadDisabled }) => {
  const [templates, setTemplates] = useState<QueueTemplateSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | undefined>(undefined);
  const [pendingDelete, setPendingDelete] = useState<QueueTemplateSummary | undefined>(undefined);

  const refresh = async () => {
    setLoading(true);
    try {
      setTemplates(await listQueueTemplates());
      setError(undefined);
    } catch (err: unknown) {
      setTemplates([]);
      setError(err instanceof Error ? err.message : 'Failed to load templates');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (open) void refresh();
  }, [open]);

  if (!open) return null;

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    try {
      await deleteQueueTemplate(pendingDelete.id);
      setPendingDelete(undefined);
      await refresh();
    } catch (err: unknown) {
      setPendingDelete(undefined);
      setError(err instanceof Error ? err.message : 'Failed to delete template');
    }
  };

  return (
    <section className="queue-template-section" aria-label="Load template">
      <h4>Load template</h4>
      {error && <div className="form-error" role="alert">{error}</div>}
      {loading && <div className="form-hint">Loading…</div>}
      {!loading && templates.length === 0 && (
        <div className="form-hint" role="status">No templates saved yet.</div>
      )}
      <ul className="template-list">
        {templates.map((t) => (
          <li key={t.id} className="template-row" data-testid="template-row">
            <span className="template-name">{t.name}</span>
            <span className="template-actions">
              <button type="button" disabled={loadDisabled} onClick={() => onLoad(t.id)}>Load</button>
              <button type="button" className="btn btn-danger" onClick={() => setPendingDelete(t)}>Delete</button>
            </span>
          </li>
        ))}
      </ul>
      <div className="queue-template-section-actions">
        <button type="button" className="btn btn-secondary" onClick={onClose}>Close</button>
      </div>
      <ConfirmDeleteModal
        open={pendingDelete !== undefined}
        itemName={pendingDelete?.name}
        onCancel={() => setPendingDelete(undefined)}
        onConfirm={() => void confirmDelete()}
      />
    </section>
  );
};
