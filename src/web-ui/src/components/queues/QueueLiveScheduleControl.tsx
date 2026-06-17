import React, { useState } from 'react';
import { SequenceDto } from '../../services/sequences';
import { LiveScheduleResult } from '../../services/queues';

type QueueLiveScheduleControlProps = {
  sequences: SequenceDto[];
  onSchedule: (sequenceId: string, offset: string) => Promise<LiveScheduleResult>;
  disabled?: boolean;
};

const pad2 = (n: number): string => n.toString().padStart(2, '0');

type Pending = { sequenceId: string; sequenceName: string; expectedFireAt: string };

/**
 * Running-queue control to schedule a library sequence to fire after a relative offset (feature
 * 059, US3). The fire time is shown as the <b>expected (earliest)</b> instant — actual firing is the
 * first iteration boundary at or after it (FR-020).
 */
export const QueueLiveScheduleControl: React.FC<QueueLiveScheduleControlProps> = ({ sequences, onSchedule, disabled }) => {
  const [sequenceId, setSequenceId] = useState('');
  const [minutes, setMinutes] = useState(0);
  const [seconds, setSeconds] = useState(0);
  const [error, setError] = useState<string | undefined>(undefined);
  const [pending, setPending] = useState<Pending[]>([]);

  const submit = async () => {
    setError(undefined);
    if (!sequenceId) {
      setError('Select a sequence to schedule.');
      return;
    }
    if (minutes < 0 || seconds < 0) {
      setError('Offset must be non-negative.');
      return;
    }
    if (seconds > 59) {
      setError('Seconds must be between 0 and 59.');
      return;
    }
    const offset = `00:${pad2(minutes)}:${pad2(seconds)}`;
    try {
      const result = await onSchedule(sequenceId, offset);
      const name = sequences.find((s) => s.id === sequenceId)?.name ?? sequenceId;
      setPending((prev) => [...prev, { sequenceId, sequenceName: name, expectedFireAt: result.expectedFireAt }]);
    } catch (err: any) {
      setError(err?.message ?? 'Failed to schedule sequence.');
    }
  };

  return (
    <section className="queue-live-schedule" aria-label="Schedule a sequence">
      <h4>Schedule a sequence</h4>
      <div className="queue-live-schedule-form">
        <select
          aria-label="Sequence to schedule"
          value={sequenceId}
          disabled={disabled}
          onChange={(e) => setSequenceId(e.target.value)}
        >
          <option value="">Select a sequence…</option>
          {sequences.map((s) => (
            <option key={s.id} value={s.id}>{s.name || s.id}</option>
          ))}
        </select>
        <input
          type="number"
          min={0}
          aria-label="Schedule offset minutes"
          value={minutes}
          disabled={disabled}
          onChange={(e) => setMinutes(parseInt(e.target.value, 10) || 0)}
        />
        <span aria-hidden="true">min</span>
        <input
          type="number"
          min={0}
          max={59}
          aria-label="Schedule offset seconds"
          value={seconds}
          disabled={disabled}
          onChange={(e) => setSeconds(parseInt(e.target.value, 10) || 0)}
        />
        <span aria-hidden="true">sec</span>
        <button type="button" disabled={disabled} onClick={() => void submit()}>Schedule</button>
      </div>
      {error && <div className="form-error" role="alert">{error}</div>}
      {pending.length > 0 && (
        <ul className="queue-live-schedule-pending" aria-label="Pending live schedules">
          {pending.map((p, i) => (
            <li key={`${p.sequenceId}-${i}`} data-testid="pending-schedule">
              {p.sequenceName} — expected (earliest) {new Date(p.expectedFireAt).toLocaleTimeString()}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
};
