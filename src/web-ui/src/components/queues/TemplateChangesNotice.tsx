import React, { useCallback, useEffect, useRef, useState } from 'react';
import { usePolling } from '../../hooks/usePolling';
import { getTemplateDiff } from '../../services/queues';
import { ReviewTemplateChangesDialog } from './ReviewTemplateChangesDialog';
import './TemplateChangesNotice.css';

/** How often the notice reads the differences between the running queue and its template. */
const POLL_INTERVAL_MS = 5000;

export type TemplateChangesNoticeProps = {
  queueId: string;
  /** False when the queue has no linked template. The notice then shows nothing and reads nothing. */
  hasTemplate: boolean;
  /** True while the queue is "paused (between sequences)". The review applies changes only then. */
  paused: boolean;
  /** Change this value to read the differences again, for example after a template save. */
  refreshKey?: number;
  /** False stops the reads, for example while the connection is lost. Default true. */
  enabled?: boolean;
  /** Called after changes are applied, so that the parent can read its own data again. */
  onChanged?: () => void;
};

type DiffState = { count: number; needsUpgrade: boolean };

/**
 * The notice of waiting template changes (feature 173). The Edit page and the phone detail both show it. It says how many
 * changes of the template wait for the running queue, and it opens the review. It shows nothing when no change waits.
 */
export const TemplateChangesNotice: React.FC<TemplateChangesNoticeProps> = ({
  queueId,
  hasTemplate,
  paused,
  refreshKey = 0,
  enabled = true,
  onChanged,
}) => {
  const [state, setState] = useState<DiffState | null>(null);
  const [reviewOpen, setReviewOpen] = useState(false);
  const active = hasTemplate && enabled;

  const read = useCallback(async () => {
    try {
      const diff = await getTemplateDiff(queueId);
      if (!diff) return;
      setState({ count: diff.differences?.length ?? 0, needsUpgrade: diff.needsUpgrade === true });
    } catch {
      // A failed read keeps the last state. The next read tries again.
    }
  }, [queueId]);

  usePolling(() => { void read(); }, { intervalMs: POLL_INTERVAL_MS, enabled: active });

  // A new refresh key reads at once. The first value is read by the polling above.
  const firstKey = useRef(refreshKey);
  useEffect(() => {
    if (refreshKey === firstKey.current || !active) return;
    void read();
  }, [refreshKey, active, read]);

  if (!hasTemplate) return null;
  const waiting = state !== null && (state.needsUpgrade || state.count > 0);
  // The review stays open after an apply, so that the operator sees the result.
  if (!waiting && !reviewOpen) return null;

  return (
    <div className="template-changes-notice" data-testid="template-changes-notice">
      {waiting && state && (
        <div className="template-changes-notice__row" role="status">
          <span>
            {state.needsUpgrade
              ? 'The template needs an upgrade before its changes can apply.'
              : `${state.count === 1 ? '1 change waits' : `${state.count} changes wait`} in the template.`}
          </span>
          {!reviewOpen && <button type="button" onClick={() => setReviewOpen(true)}>Review changes</button>}
        </div>
      )}
      {reviewOpen && (
        <ReviewTemplateChangesDialog
          queueId={queueId}
          paused={paused}
          onClose={() => setReviewOpen(false)}
          onApplied={() => { void read(); onChanged?.(); }}
        />
      )}
    </div>
  );
};
