import React, { useCallback, useEffect, useState } from 'react';
import { NotificationLevel, QueueDto, listQueues, setQueueNotificationLevel } from '../../services/queues';
import { useLayoutMode } from '../../hooks/useLayoutMode';

const levels: { value: NotificationLevel; label: string }[] = [
  { value: 'none', label: 'None' },
  { value: 'failure', label: 'Failure' },
  { value: 'successAndFailure', label: 'Success + Failure' }
];

/**
 * Table of all queues with a notification level select (feature 120). A change calls the level
 * route one time. The new level applies to the next sequence that finishes in the queue.
 */
export const QueueLevelTable: React.FC = () => {
  const phone = useLayoutMode() === 'phone';
  const [queues, setQueues] = useState<QueueDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setQueues((await listQueues()) ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The queues could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const change = async (queue: QueueDto, level: NotificationLevel) => {
    setError(null);
    try {
      const saved = await setQueueNotificationLevel(queue.id, level);
      setQueues((prev) => prev.map((q) => (q.id === queue.id ? { ...q, notificationLevel: saved?.notificationLevel ?? level } : q)));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The level could not be saved.');
    }
  };

  if (loading) return <p>Wait. The queues load.</p>;

  const levelSelect = (queue: QueueDto) => (
    <select
      aria-label={`Notification level for ${queue.name}`}
      value={queue.notificationLevel ?? 'none'}
      onChange={(e) => void change(queue, e.target.value as NotificationLevel)}
    >
      {levels.map((l) => (
        <option key={l.value} value={l.value}>{l.label}</option>
      ))}
    </select>
  );

  return (
    <section aria-label="Queue notification levels">
      <h3>Queue notification levels</h3>
      {error && <div role="alert" className="form-error">{error}</div>}
      {queues.length === 0 ? (
        <p className="form-hint">No queue exists.</p>
      ) : phone ? (
        <ul className="phone-card-list">
          {queues.map((queue) => (
            <li key={queue.id} className="phone-row-card">
              <span className="phone-card__title" title={queue.name}>{queue.name}</span>
              {levelSelect(queue)}
            </li>
          ))}
        </ul>
      ) : (
        <table className="queue-level-table">
          <thead>
            <tr>
              <th>Queue</th>
              <th>Level</th>
            </tr>
          </thead>
          <tbody>
            {queues.map((queue) => (
              <tr key={queue.id}>
                <td>{queue.name}</td>
                <td>{levelSelect(queue)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
};
