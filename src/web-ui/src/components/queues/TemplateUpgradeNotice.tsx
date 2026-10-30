import React, { useCallback, useEffect, useState } from 'react';
import { getQueueTemplate } from '../../services/queueTemplates';
import { TemplateUpgradePanel } from './TemplateUpgradePanel';
import './TemplateUpgradePanel.css';

export type TemplateUpgradeNoticeProps = {
  templateId: string;
  /** Called after the upgrade. */
  onUpgraded?: () => void;
};

/**
 * Marks a template that needs an upgrade (feature 171) and offers the manual upgrade. It reads the template itself and
 * shows nothing for a template that has its entry IDs, or that cannot be read.
 */
export const TemplateUpgradeNotice: React.FC<TemplateUpgradeNoticeProps> = ({ templateId, onUpgraded }) => {
  const [needsUpgrade, setNeedsUpgrade] = useState(false);

  const read = useCallback(async () => {
    try {
      const template = await getQueueTemplate(templateId);
      setNeedsUpgrade(template?.needsUpgrade === true);
    } catch {
      setNeedsUpgrade(false);
    }
  }, [templateId]);

  useEffect(() => {
    void read();
  }, [read]);

  if (!needsUpgrade) return null;

  return (
    <div className="template-upgrade-notice" data-testid="template-upgrade-notice">
      <span className="template-upgrade-notice__mark" data-testid="needs-upgrade-mark">Needs upgrade</span>
      <p className="form-hint">
        This template was saved by an older release. It works as before. To change a running queue from it, upgrade it first.
      </p>
      <TemplateUpgradePanel
        templateId={templateId}
        onDone={() => {
          void read();
          onUpgraded?.();
        }}
      />
    </div>
  );
};
