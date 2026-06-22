import React, { useEffect, useState } from 'react';
import { QueueStatus } from '../../services/queues';
import { ApiError } from '../../lib/api';
import { TemplatePickerDialog } from './TemplatePickerDialog';

/** An inline confirmation prompt shown between the template header and sequences. */
export type TemplateConfirm = {
  title: string;
  message: string;
  confirmText: string;
  onConfirm: () => void;
  onCancel: () => void;
};

type QueueTemplateControlsProps = {
  /** Name of the template the queue is currently associated with (loaded or saved this session). */
  associatedTemplateName?: string;
  status: QueueStatus;
  /** Persists the current queue entries as a template; rejects with ApiError(409) when the name exists. */
  onSaveTemplate: (name: string, overwrite: boolean) => Promise<void>;
  /** Loads the chosen template's entries into the queue. */
  onLoadTemplate: (templateId: string) => void;
  /** Re-applies the associated template's persisted entries (with diff-aware confirmation). */
  onReload: () => void;
  /** When set, a replace/reload confirmation is shown between the template header and sequences. */
  pendingConfirm?: TemplateConfirm;
  /** Inline save outcome shown at the Save Template row (co-located with the Save Template button). */
  saveResult?: { kind: 'success' | 'error'; message: string };
  /** Sequence entries to render between the template header and the Save/Reload action row. */
  children?: React.ReactNode;
};

const NAME_PATTERN = /^[A-Za-z0-9 _-]+$/;

const validateName = (raw: string): string | undefined => {
  const name = raw.trim();
  if (!name) return 'Name is required';
  if (name.length > 100) return 'Name must be 100 characters or fewer';
  if (!NAME_PATTERN.test(name)) return 'Name may contain only letters, digits, spaces, hyphens, and underscores';
  return undefined;
};

export const QueueTemplateControls: React.FC<QueueTemplateControlsProps> = ({
  associatedTemplateName,
  status,
  onSaveTemplate,
  onLoadTemplate,
  onReload,
  pendingConfirm,
  saveResult,
  children,
}) => {
  // The "manage" area (opened from the template name button) lets the user pick a template to load
  // and edit the name the queue will be saved under.
  const [open, setOpen] = useState(false);
  const [templateName, setTemplateName] = useState(associatedTemplateName ?? '');
  const [nameError, setNameError] = useState<string | undefined>(undefined);
  const [confirmingOverwrite, setConfirmingOverwrite] = useState(false);
  const [saving, setSaving] = useState(false);
  const running = status === 'Running';

  // Keep the editable name in sync with the queue's associated template (initial load, after a
  // load/save changes the association).
  useEffect(() => {
    setTemplateName(associatedTemplateName ?? '');
    setNameError(undefined);
    setConfirmingOverwrite(false);
  }, [associatedTemplateName]);

  const closeManage = () => setOpen(false);

  // Opening the manage area resets the editable name to the current template so a previous
  // unsaved edit (where the user never clicked Rename) does not linger.
  const toggleManage = () =>
    setOpen((current) => {
      if (!current) {
        setTemplateName(associatedTemplateName ?? '');
        setNameError(undefined);
        setConfirmingOverwrite(false);
      }
      return !current;
    });

  const persist = async (name: string, overwrite: boolean) => {
    setSaving(true);
    setNameError(undefined);
    try {
      await onSaveTemplate(name, overwrite);
      setConfirmingOverwrite(false);
      setOpen(false);
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 409) {
        setConfirmingOverwrite(true);
      } else {
        setNameError(err instanceof Error ? err.message : 'Failed to save template');
      }
    } finally {
      setSaving(false);
    }
  };

  // Rename: save under the name the user typed in the manage area. Saving back to the associated
  // template (name unchanged, case-insensitive) is a one-click overwrite; a brand-new name succeeds
  // in one action; a collision with a *different* existing template asks to confirm (409), with the
  // confirmation shown next to the Rename button.
  const handleRename = () => {
    const validation = validateName(templateName);
    if (validation) {
      setNameError(validation);
      return;
    }
    const norm = (x: string) => x.trim().toLowerCase();
    const unchanged = Boolean(associatedTemplateName) && norm(templateName) === norm(associatedTemplateName as string);
    void persist(templateName.trim(), unchanged);
  };

  // Save Template (bottom): quick re-save to the currently associated template under its existing
  // name, ignoring any unsaved edit in the name field. Disabled when there is no template yet.
  const handleQuickSave = () => {
    if (!associatedTemplateName) return;
    void persist(associatedTemplateName, true);
  };

  return (
    <section className="queue-template-controls" aria-label="Queue templates">
      {/* Template name — clicking opens the manage area (load picker + name editing) */}
      <div className="queue-template-row">
        <span className="queue-section-label">Template</span>
        <button
          type="button"
          className="link-button queue-template-name"
          onClick={toggleManage}
          aria-expanded={open}
        >
          {associatedTemplateName ?? '(no template)'}
        </button>
      </div>

      <TemplatePickerDialog
        open={open && !pendingConfirm}
        loadDisabled={running}
        name={templateName}
        onNameChange={(value) => {
          setTemplateName(value);
          setNameError(undefined);
        }}
        onSubmitName={handleRename}
        submitDisabled={saving}
        nameError={nameError}
        confirmingOverwrite={confirmingOverwrite}
        onConfirmOverwrite={() => void persist(templateName.trim(), true)}
        onCancelOverwrite={() => setConfirmingOverwrite(false)}
        onLoad={(templateId) => {
          onLoadTemplate(templateId);
          closeManage();
        }}
        onClose={closeManage}
      />

      {pendingConfirm && (
        <section className="queue-template-section" aria-label={pendingConfirm.title}>
          <h4>{pendingConfirm.title}</h4>
          <p>{pendingConfirm.message}</p>
          <div className="queue-template-section-actions">
            <button type="button" className="btn btn-secondary" onClick={pendingConfirm.onCancel}>Cancel</button>
            <button type="button" className="btn btn-primary" onClick={pendingConfirm.onConfirm}>{pendingConfirm.confirmText}</button>
          </div>
        </section>
      )}

      {/* Sequence entries */}
      {children}

      {/* Template action buttons below sequences */}
      <div className="queue-template-row">
        <button
          type="button"
          onClick={handleQuickSave}
          disabled={saving || !associatedTemplateName}
          title={
            !associatedTemplateName
              ? 'Name and save a new template with the template name field above.'
              : `Save changes to "${associatedTemplateName}".`
          }
        >
          Save Template
        </button>
        <button
          type="button"
          onClick={onReload}
          disabled={!associatedTemplateName || running}
          title={
            !associatedTemplateName
              ? 'No template to reload.'
              : running
                ? 'Stop the queue before reloading a template.'
                : undefined
          }
        >
          Reload Template
        </button>
      </div>

      {/* When the manage area is open the name error is shown inline with the field; here it
          covers the closed-area case (e.g. clicking Save Template with no name entered). */}
      {!open && nameError && <div className="form-error" role="alert">{nameError}</div>}
      {saveResult && (
        saveResult.kind === 'success'
          ? <div className="form-hint" role="status">{saveResult.message}</div>
          : <div className="form-error" role="alert">{saveResult.message}</div>
      )}
    </section>
  );
};
