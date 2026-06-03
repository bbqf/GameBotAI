import React, { useState } from 'react';
import { QueueStatus } from '../../services/queues';
import { SaveTemplateDialog } from './SaveTemplateDialog';
import { TemplatePickerDialog } from './TemplatePickerDialog';

type OpenSection = 'none' | 'save' | 'load';

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
  /** Sequence entries to render between the template header and the Save/Reload action row. */
  children?: React.ReactNode;
};

export const QueueTemplateControls: React.FC<QueueTemplateControlsProps> = ({
  associatedTemplateName,
  status,
  onSaveTemplate,
  onLoadTemplate,
  onReload,
  pendingConfirm,
  children,
}) => {
  const [openSection, setOpenSection] = useState<OpenSection>('none');
  const running = status === 'Running';
  const section = pendingConfirm ? 'none' : openSection;

  const toggle = (s: Exclude<OpenSection, 'none'>) =>
    setOpenSection((current) => (current === s ? 'none' : s));

  const close = () => setOpenSection('none');

  return (
    <section className="queue-template-controls" aria-label="Queue templates">
      {/* Template name — clicking opens the load picker */}
      <div className="queue-template-row">
        <button
          type="button"
          className="link-button queue-template-name"
          onClick={() => toggle('load')}
          aria-expanded={openSection === 'load'}
        >
          {associatedTemplateName ?? '(no template)'}
        </button>
      </div>

      <SaveTemplateDialog
        open={section === 'save'}
        originName={associatedTemplateName}
        onSave={onSaveTemplate}
        onClose={close}
      />

      <TemplatePickerDialog
        open={section === 'load'}
        loadDisabled={running}
        onLoad={(templateId) => {
          onLoadTemplate(templateId);
          close();
        }}
        onClose={close}
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
        <button type="button" onClick={() => toggle('save')} aria-expanded={openSection === 'save'}>
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
    </section>
  );
};
