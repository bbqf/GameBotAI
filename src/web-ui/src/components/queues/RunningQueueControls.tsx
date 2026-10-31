import React, { useCallback, useState } from 'react';
import { usePolling } from '../../hooks/usePolling';
import { getQueueHealth, QueueHealthDto } from '../../services/queues';
import { QueuePauseControls } from './QueuePauseControls';
import { TemplateChangesNotice } from './TemplateChangesNotice';

/** How often the pause state is read while the Edit page of a running queue is open. */
const HEALTH_POLL_INTERVAL_MS = 2500;

export type RunningQueueControlsProps = {
  queueId: string;
  /** False when the queue has no linked template. The notice then shows nothing. */
  hasTemplate: boolean;
  /** Change this value to read the waiting changes again, for example after a template save. */
  refreshKey?: number;
  /** Called after a pause, a resume, or an apply. */
  onChanged?: () => void;
};

/**
 * The pause controls and the notice of waiting template changes, for the Edit page of a running queue (feature 173).
 * The component reads the pause state once and gives it to both parts, so the review knows when the queue is paused.
 */
export const RunningQueueControls: React.FC<RunningQueueControlsProps> = ({ queueId, hasTemplate, refreshKey, onChanged }) => {
  const [health, setHealth] = useState<QueueHealthDto | null>(null);

  const read = useCallback(() => {
    Promise.resolve()
      .then(() => getQueueHealth(queueId))
      .then((next) => setHealth(next ?? null))
      .catch(() => {
        // A failed read keeps the last state. The next read tries again.
      });
  }, [queueId]);

  usePolling(read, { intervalMs: HEALTH_POLL_INTERVAL_MS, enabled: true });

  const changed = () => {
    read();
    onChanged?.();
  };

  return (
    <div className="running-queue-controls">
      <QueuePauseControls queueId={queueId} health={health} onChanged={changed} />
      <TemplateChangesNotice
        queueId={queueId}
        hasTemplate={hasTemplate}
        paused={health?.operatorPause === 'paused'}
        refreshKey={refreshKey}
        onChanged={changed}
      />
    </div>
  );
};
