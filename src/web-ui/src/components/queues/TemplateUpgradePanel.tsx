import React, { useState } from 'react';
import { apiErrorHint } from '../../services/queues';
import {
  previewQueueTemplateUpgrade,
  upgradeQueueTemplate,
  TemplateUpgradePreview
} from '../../services/queueTemplates';
import './TemplateUpgradePanel.css';

export type TemplateUpgradePanelProps = {
  templateId: string;
  /** Called after the upgrade, so that the parent can read the template again. */
  onDone?: () => void;
};

type Problem = { message: string; hint: string | null };

const toProblem = (err: unknown): Problem => {
  const payload = (err as { payload?: { error?: { message?: unknown } } } | null)?.payload;
  const serverMessage = payload?.error?.message;
  const message = typeof serverMessage === 'string' && serverMessage.length > 0
    ? serverMessage
    : err instanceof Error && err.message ? err.message : 'The upgrade failed.';
  return { message, hint: apiErrorHint(err) };
};

const entriesText = (n: number): string => `${n} ${n === 1 ? 'entry' : 'entries'}`;

/**
 * The manual upgrade of a template that was saved before feature 171 (it has no entry IDs). The operator first reads
 * what the upgrade does, and then confirms. The upgrade gives an ID to each entry and changes nothing else. For a
 * running queue that uses the template, it gives the same IDs to the running entries, with no change to any schedule.
 * A repeat changes 0 entries.
 */
export const TemplateUpgradePanel: React.FC<TemplateUpgradePanelProps> = ({ templateId, onDone }) => {
  const [preview, setPreview] = useState<TemplateUpgradePreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [changed, setChanged] = useState<number | null>(null);

  const open = async () => {
    setBusy(true);
    setProblem(null);
    setChanged(null);
    try {
      setPreview(await previewQueueTemplateUpgrade(templateId));
    } catch (err) {
      setProblem(toProblem(err));
    } finally {
      setBusy(false);
    }
  };

  const confirm = async () => {
    if (!preview) return;
    setBusy(true);
    setProblem(null);
    try {
      const result = await upgradeQueueTemplate(templateId, preview.runningMatch.length > 0);
      setChanged(result.entriesChanged);
      setPreview(null);
      onDone?.();
    } catch (err) {
      setProblem(toProblem(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="template-upgrade" data-testid="template-upgrade">
      {problem && (
        <div className="form-error" role="alert">
          <p>{problem.message}</p>
          {problem.hint && <p>{problem.hint}</p>}
        </div>
      )}
      {changed !== null && (
        <p className="form-hint" role="status" data-testid="upgrade-done">
          {changed === 0
            ? 'The template has its entry IDs already. 0 entries changed.'
            : `Upgrade done. ${entriesText(changed)} got an entry ID.`}
        </p>
      )}
      {preview && (
        <div data-testid="upgrade-preview">
          <p>{entriesText(preview.entriesToChange)} get an entry ID. No other field of the template changes.</p>
          {preview.runningMatch.length > 0 && (
            <div data-testid="upgrade-running-match">
              <p>
                {entriesText(preview.runningMatch.length)} of running queues get the same IDs. Their schedules do not change.
              </p>
              <ul>
                {preview.runningMatch.slice(0, 10).map((m) => (
                  <li key={`${m.queueId}-${m.position}`} data-testid="upgrade-match-row">
                    Position {m.position + 1}: {m.sequenceName}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {preview.unmatched.length > 0 && (
            <div className="form-error" role="alert" data-testid="upgrade-unmatched">
              <p>The upgrade cannot match these running entries to the template. Stop the queue or reload the template, then try again.</p>
              <ul>
                {preview.unmatched.slice(0, 10).map((u) => (
                  <li key={`${u.queueId}-${u.position}`}>Position {u.position + 1}: {u.reason}</li>
                ))}
              </ul>
            </div>
          )}
          <div className="template-upgrade__actions">
            {preview.unmatched.length === 0 && (
              <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void confirm()}>Confirm upgrade</button>
            )}
            <button type="button" className="btn btn-secondary" disabled={busy} onClick={() => setPreview(null)}>Cancel</button>
          </div>
        </div>
      )}
      {!preview && changed === null && (
        <div className="template-upgrade__actions">
          <button type="button" disabled={busy} onClick={() => void open()}>Upgrade template</button>
        </div>
      )}
    </div>
  );
};
