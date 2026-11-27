import React, { useCallback, useEffect, useState } from 'react';
import { NotificationLevel, QueueDto, listQueues, setQueueNotificationLevel } from '../../services/queues';
import { useLayoutMode, type LayoutMode } from '../../hooks/useLayoutMode';
import { ListView, SectionHeader, type ListColumn, type ListState } from '../ui';

const levels: { value: NotificationLevel; label: string }[] = [
  { value: 'none', label: 'None' },
  { value: 'failure', label: 'Failure' },
  { value: 'successAndFailure', label: 'Success + Failure' }
];

/**
 * Table of all queues with a notification level select (feature 120). A change calls the level
 * route one time. The new level applies to the next sequence that finishes in the queue.
 */
export const QueueLevelTable: React.FC<{ mode?: LayoutMode }> = ({ mode }) => {
  const layout = useLayoutMode();
  const phone = (mode ?? layout) === 'phone';
  const [queues, setQueues] = useState<QueueDto[]>([]);
  const [loading, setLoading] = useState(true);
  // A load error shows in the list. A change error shows under the section header.
  const [loadError, setLoadError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      setQueues((await listQueues()) ?? []);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'The queues could not be loaded.');
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

  const levelSelect = (queue: QueueDto, className: string) => (
    <select
      className={className}
      aria-label={`Notification level for ${queue.name}`}
      value={queue.notificationLevel ?? 'none'}
      onChange={(e) => void change(queue, e.target.value as NotificationLevel)}
    >
      {levels.map((l) => (
        <option key={l.value} value={l.value}>{l.label}</option>
      ))}
    </select>
  );

  const columns: ListColumn<QueueDto>[] = [
    { id: 'queue', header: 'Queue', width: 'flex', render: (q) => q.name, text: (q) => q.name },
    { id: 'level', header: 'Level', width: 240, render: (q) => levelSelect(q, 'ui-field__input') }
  ];

  let state: ListState = 'ready';
  if (loading) state = { kind: 'loading' };
  else if (loadError) state = { kind: 'error', text: loadError, onRetry: () => void load() };
  else if (queues.length === 0) state = { kind: 'empty', text: 'No queue exists.' };

  return (
    <section aria-label="Queue notification levels">
      <SectionHeader title="Queue notification levels" />
      {error && <div role="alert" className="form-error">{error}</div>}
      <ListView
        ariaLabel="Queue notification levels"
        tableClassName="queue-level-table"
        columns={columns}
        rows={queues}
        getRowKey={(q) => q.id}
        state={state}
        mode={mode}
        rowTestId={(q) => `level-row-${q.id}`}
        phone={{ title: (q) => q.name }}
        renderPhoneActions={phone ? (q) => levelSelect(q, 'ui-field__input ui-level-select--phone') : undefined}
      />
    </section>
  );
};
