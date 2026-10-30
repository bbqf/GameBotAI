import React, { useCallback, useEffect, useRef, useState } from 'react';
import { usePolling } from '../../hooks/usePolling';
import { getQueueHealth, pauseQueue, resumeQueue, QueueHealthDto, OperatorPauseState } from '../../services/queues';
import { ReviewTemplateChangesDialog } from './ReviewTemplateChangesDialog';
import './QueuePauseControls.css';

/** How often the state of the pause is read while the queue runs. */
const POLL_INTERVAL_MS = 2500;

export type QueuePauseControlsProps = {
  queueId: string;
  /**
   * The health block of the queue, when the parent already reads it (the phone view does). The component then reads
   * nothing itself. Leave it out, and the component reads the health about every 2.5 s.
   */
  health?: QueueHealthDto | null;
  /** False stops the reads, for example while the connection is lost. Default true. */
  enabled?: boolean;
  /** Called after a pause, a resume, or a cancel, so that the parent can read its own data again. */
  onChanged?: () => void;
  /** Shows the "Review changes" button, which opens the review of the differences between the queue and its template. */
  reviewChanges?: boolean;
  /** Extra controls that belong beside the pause buttons. */
  children?: React.ReactNode;
};

const errorText = (err: unknown): string => (err instanceof Error && err.message ? err.message : 'The request failed.');

const formatSince = (iso: string | null | undefined): string => {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
};

/**
 * Pause and resume of a running queue (feature 171). "Pause" lets the running sequence finish. Then the queue waits
 * between sequences and starts no new one until "Resume". A pause that waits for the running sequence shows
 * "Pausing", and the operator can cancel it. The component shows nothing when the queue does not run. It is not
 * the "Pause when idle" setting of the queue.
 */
export const QueuePauseControls: React.FC<QueuePauseControlsProps> = ({ queueId, health: healthProp, enabled = true, onChanged, reviewChanges = false, children }) => {
  const [reviewOpen, setReviewOpen] = useState(false);
  const controlled = healthProp !== undefined;
  const [health, setHealth] = useState<QueueHealthDto | null>(controlled ? healthProp : null);
  const [state, setState] = useState<OperatorPauseState | null>(controlled && healthProp ? healthProp.operatorPause ?? 'none' : null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>(undefined);
  const busyRef = useRef(false);

  const apply = useCallback((next: QueueHealthDto | null) => {
    setHealth(next);
    setState(next ? next.operatorPause ?? 'none' : null);
  }, []);

  // A parent that reads the health itself gives the newest value here.
  useEffect(() => {
    if (healthProp !== undefined && !busyRef.current) apply(healthProp);
  }, [healthProp, apply]);

  const read = useCallback(() => {
    if (busyRef.current) return;
    Promise.resolve()
      .then(() => getQueueHealth(queueId))
      .then((next) => {
        if (busyRef.current) return;
        apply(next ?? null);
      })
      .catch(() => {
        // A failed read keeps the last state. The next read tries again.
      });
  }, [queueId, apply]);

  usePolling(read, { intervalMs: POLL_INTERVAL_MS, enabled: enabled && !controlled });

  const run = async (call: () => Promise<void>) => {
    busyRef.current = true;
    setBusy(true);
    setError(undefined);
    try {
      await call();
      onChanged?.();
    } catch (err) {
      setError(errorText(err));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };

  const onPause = () => run(async () => {
    const result = await pauseQueue(queueId);
    setState(result.operatorPause);
  });

  const onResume = () => run(async () => {
    await resumeQueue(queueId);
    setState('none');
  });

  if (!health) return null;

  const current: OperatorPauseState = state ?? 'none';
  return (
    <div className="queue-pause-controls" data-testid="queue-pause-controls">
      {current === 'pausing' && (
        <p className="queue-pause-controls__state" role="status" data-testid="queue-pause-state">
          Pausing (waiting for the running sequence)
        </p>
      )}
      {current === 'paused' && (
        <p className="queue-pause-controls__state queue-pause-controls__state--paused" role="status" data-testid="queue-pause-state">
          Paused (between sequences)
          {health.pausedAt ? ` — since ${formatSince(health.pausedAt)}` : ''}
        </p>
      )}
      {current === 'none' && (
        <button type="button" disabled={busy} onClick={() => void onPause()}>Pause</button>
      )}
      {current === 'pausing' && (
        <button type="button" disabled={busy} onClick={() => void onResume()}>Cancel pause</button>
      )}
      {current === 'paused' && (
        <button type="button" disabled={busy} onClick={() => void onResume()}>Resume</button>
      )}
      {reviewChanges && !reviewOpen && (
        <button type="button" onClick={() => setReviewOpen(true)}>Review changes</button>
      )}
      {children}
      {error && <p className="form-error queue-pause-controls__error" role="alert">{error}</p>}
      {reviewChanges && reviewOpen && (
        <div className="queue-pause-controls__review">
          <ReviewTemplateChangesDialog
            queueId={queueId}
            paused={current === 'paused'}
            onClose={() => setReviewOpen(false)}
            onApplied={() => onChanged?.()}
          />
        </div>
      )}
    </div>
  );
};
