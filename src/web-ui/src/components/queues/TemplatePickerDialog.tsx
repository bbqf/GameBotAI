import React, { useEffect, useState } from 'react';
import { QueueTemplateSummary, listQueueTemplates, deleteQueueTemplate } from '../../services/queueTemplates';
import { ConfirmDeleteModal } from '../ConfirmDeleteModal';

type TemplatePickerDialogProps = {
  open: boolean;
  onLoad: (templateId: string) => void;
  onClose: () => void;
  /** Disables the Load action (e.g. the queue is running). */
  loadDisabled?: boolean;
  /** Editable template name used by the Rename action; when provided, a name field is shown. */
  name?: string;
  onNameChange?: (value: string) => void;
  /** Applies the typed name (Rename button / Enter in the name field). */
  onSubmitName?: () => void;
  /** Disables the Rename action (e.g. while a save is in flight). */
  submitDisabled?: boolean;
  /** Validation/save error for the name field, shown beneath it. */
  nameError?: string;
  /** When true, an overwrite confirmation is shown next to the Rename button. */
  confirmingOverwrite?: boolean;
  onConfirmOverwrite?: () => void;
  onCancelOverwrite?: () => void;
};

export const TemplatePickerDialog: React.FC<TemplatePickerDialogProps> = ({ open, onLoad, onClose, loadDisabled, name, onNameChange, onSubmitName, submitDisabled, nameError, confirmingOverwrite, onConfirmOverwrite, onCancelOverwrite }) => {
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
      {onNameChange && (
        <div className="queue-template-name-edit">
          <label htmlFor="template-name">Template name</label>
          <input
            id="template-name"
            type="text"
            value={name ?? ''}
            onChange={(e) => onNameChange(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && onSubmitName) {
                e.preventDefault();
                onSubmitName();
              }
            }}
          />
          <button type="button" className="btn btn-primary" onClick={onSubmitName} disabled={submitDisabled}>Rename</button>
        </div>
      )}
      {nameError && <div className="form-error" role="alert">{nameError}</div>}
      {confirmingOverwrite && (
        <section className="queue-template-section" aria-label="Confirm overwrite">
          <p>
            A template named <strong>{name?.trim()}</strong> already exists. Overwrite it?
          </p>
          <div className="queue-template-section-actions">
            <button type="button" className="btn btn-danger" onClick={onConfirmOverwrite} disabled={submitDisabled}>Overwrite</button>
            <button type="button" className="btn btn-secondary" onClick={onCancelOverwrite} disabled={submitDisabled}>Cancel</button>
          </div>
        </section>
      )}
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
      {!onNameChange && (
        <div className="queue-template-section-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose}>Close</button>
        </div>
      )}
      <ConfirmDeleteModal
        open={pendingDelete !== undefined}
        itemName={pendingDelete?.name}
        onCancel={() => setPendingDelete(undefined)}
        onConfirm={() => void confirmDelete()}
      />
    </section>
  );
};
