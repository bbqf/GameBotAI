import React, { useCallback, useEffect, useRef, useState } from 'react';
import { getQueueMonitor, QueueMonitorDto, QueueMonitorItemDto } from '../../services/queues';
import './QueueMonitor.css';

// Poll interval for the live monitor. Kept within the spec's 2–3s auto-refresh target (FR-007).
const POLL_INTERVAL_MS = 2500;

export type QueueMonitorProps = {
  queueId: string;
  /** Invoked when the operator returns to the editor from the ended state. */
  onReturnToEditor?: () => void;
};

const formatTime = (iso: string | null): string => {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
};

const itemName = (item: QueueMonitorItemDto): string =>
  item.sequenceName ?? `Unknown sequence${item.stale ? ' (stale)' : ''}`;

const itemTiming = (item: QueueMonitorItemDto): string => {
  if (item.expectedAt) return formatTime(item.expectedAt);
  if (item.relativeLabel) return item.relativeLabel;
  return '';
};

/**
 * Read-only live "playlist" for a running queue (feature 072): the sequence running now and the
 * ordered up-next list, each with its schedule reason and expected time. Polls
 * <c>GET /api/queues/{id}/monitor</c> every ~2.5s while mounted; a poll returning <c>running:false</c>
 * switches to an "ended" state and stops polling. No run controls — start/stop/schedule stay in the
 * queues overview.
 */
export const QueueMonitor: React.FC<QueueMonitorProps> = ({ queueId, onReturnToEditor }) => {
  const [snapshot, setSnapshot] = useState<QueueMonitorDto | undefined>(undefined);
  const [error, setError] = useState<string | undefined>(undefined);
  const stoppedRef = useRef(false);

  const poll = useCallback(async () => {
    try {
      const data = await getQueueMonitor(queueId);
      setSnapshot(data);
      setError(undefined);
      if (!data.running) stoppedRef.current = true;
    } catch (err: any) {
      setError(err?.message ?? 'Failed to load monitor');
    }
  }, [queueId]);

  useEffect(() => {
    stoppedRef.current = false;
    void poll();
    const timer = setInterval(() => {
      if (stoppedRef.current) return; // run ended → stop refreshing (interval cleared on unmount)
      void poll();
    }, POLL_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [poll]);

  if (error && !snapshot) {
    return <div className="queue-monitor" data-testid="queue-monitor"><div className="form-error" role="alert">{error}</div></div>;
  }
  if (!snapshot) {
    return <div className="queue-monitor" data-testid="queue-monitor">Loading…</div>;
  }

  // Ended / not-running state (FR-010): show the last outcome and a path back to the editor + logs.
  if (!snapshot.running) {
    return (
      <div className="queue-monitor" data-testid="queue-monitor">
        <div className="queue-monitor-ended" data-testid="monitor-ended" role="status">
          <h4>Run ended</h4>
          {snapshot.lastOutcome ? (
            <p>
              <span className={`monitor-outcome monitor-outcome-${snapshot.lastOutcome.status}`}>{snapshot.lastOutcome.status}</span>
              {' — '}
              {snapshot.lastOutcome.summary}
            </p>
          ) : (
            <p>The queue is not running.</p>
          )}
          <div className="queue-monitor-actions">
            {onReturnToEditor && (
              <button type="button" onClick={onReturnToEditor}>Back to editor</button>
            )}
            <a href="#/execution">See Execution Logs</a>
          </div>
        </div>
      </div>
    );
  }

  const waiting = !snapshot.current && snapshot.upcoming.find((i) => i.relativeLabel === 'waiting' && i.expectedAt);

  return (
    <div className="queue-monitor" data-testid="queue-monitor">
      <div className="queue-monitor-header">
        <h4>{snapshot.name} — live monitor</h4>
        {snapshot.cycleExecution && <span className="monitor-badge">cycling</span>}
      </div>

      {/* "Now" row */}
      <div className="queue-monitor-now" data-testid="monitor-now">
        <span className="monitor-label">Now</span>
        {snapshot.current ? (
          <span className="monitor-current">
            <strong>{itemName(snapshot.current)}</strong>
            <span className="monitor-reason">{snapshot.current.reason}</span>
          </span>
        ) : waiting ? (
          <span className="monitor-current monitor-idle">Running — waiting until {formatTime(waiting.expectedAt)}</span>
        ) : snapshot.nothingScheduled ? (
          <span className="monitor-current monitor-idle">Running — nothing scheduled</span>
        ) : (
          <span className="monitor-current monitor-idle">Running</span>
        )}
      </div>

      {/* "Up next" list */}
      {snapshot.nothingScheduled ? (
        <div className="queue-monitor-empty" data-testid="monitor-nothing">Nothing scheduled.</div>
      ) : (
        <div className="queue-monitor-upcoming">
          <span className="monitor-label">Up next</span>
          {snapshot.upcoming.length === 0 ? (
            <div className="queue-monitor-empty">Nothing up next.</div>
          ) : (
            <ul>
              {snapshot.upcoming.map((item) => (
                <li key={`${item.order}-${item.sequenceId}`} data-testid="monitor-upcoming-item" className={item.stale ? 'monitor-stale' : undefined}>
                  <span className="monitor-seq">{itemName(item)}</span>
                  <span className="monitor-reason">{item.reason}</span>
                  <span className="monitor-when">{itemTiming(item)}</span>
                  {item.repeats && <span className="monitor-badge" title="Repeats each cycle">repeats</span>}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
};
