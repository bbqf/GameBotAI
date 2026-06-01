import React, { useState } from 'react';
import { QueueStatus } from '../../services/queues';
import { SaveTemplateDialog } from './SaveTemplateDialog';
import { TemplatePickerDialog } from './TemplatePickerDialog';

type OpenSection = 'none' | 'save' | 'load';

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
};

export const QueueTemplateControls: React.FC<QueueTemplateControlsProps> = ({
  associatedTemplateName,
  status,
  onSaveTemplate,
  onLoadTemplate,
  onReload,
}) => {
  const [openSection, setOpenSection] = useState<OpenSection>('none');
  const running = status === 'Running';

  const toggle = (section: Exclude<OpenSection, 'none'>) =>
    setOpenSection((current) => (current === section ? 'none' : section));

  const close = () => setOpenSection('none');

  return (
    <section className="queue-template-controls" aria-label="Queue templates">
      <div className="queue-template-row">
        <button
          type="button"
          className="link-button queue-template-name"
          onClick={() => toggle('load')}
          aria-expanded={openSection === 'load'}
        >
          {associatedTemplateName ?? '(no template)'}
        </button>
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

      <SaveTemplateDialog
        open={openSection === 'save'}
        originName={associatedTemplateName}
        onSave={onSaveTemplate}
        onClose={close}
      />

      <TemplatePickerDialog
        open={openSection === 'load'}
        loadDisabled={running}
        onLoad={(templateId) => {
          onLoadTemplate(templateId);
          close();
        }}
        onClose={close}
      />
    </section>
  );
};
