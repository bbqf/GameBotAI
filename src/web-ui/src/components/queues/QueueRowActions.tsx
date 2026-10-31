import React, { useRef, useState } from 'react';
import { OperatorPauseState, pauseQueue, QueueDto, resumeQueue } from '../../services/queues';
import { IconButton } from './IconButton';

export type QueueRowActionsProps = {
  queue: Pick<QueueDto, 'id' | 'name' | 'status' | 'operatorPause'>;
  onStart: (id: string) => void;
  onStop: (id: string) => void;
  onEdit: (id: string) => void;
  onDuplicate: (id: string) => void;
  onDelete: (id: string) => void;
  /** Called after a pause, a resume, or a cancel, so that the page can read the list again. */
  onChanged?: () => void;
  /** Called with the text of a refused call. */
  onError?: (message: string) => void;
};

const errorText = (err: unknown): string => (err instanceof Error && err.message ? err.message : 'The request failed.');

/**
 * The icons of one row of the Queues list (feature 173). The set depends on the state of the queue. Each icon has the
 * name "<Action> <queue name>" and the tooltip "<Action>".
 */
export const QueueRowActions: React.FC<QueueRowActionsProps> = ({ queue, onStart, onStop, onEdit, onDuplicate, onDelete, onChanged, onError }) => {
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const running = queue.status === 'Running';
  const pause: OperatorPauseState = queue.operatorPause ?? 'none';
  const name = (action: string) => `${action} ${queue.name}`;

  const runPauseCall = async (call: () => Promise<unknown>) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    try {
      await call();
      onChanged?.();
    } catch (err) {
      onError?.(errorText(err));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };

  return (
    <span className="queue-row-actions" role="group" aria-label={`Actions for ${queue.name}`}>
      {!running && <IconButton icon="play" label={name('Start')} title="Start" onClick={() => onStart(queue.id)} />}
      {running && pause === 'none' && (
        <IconButton icon="pause" label={name('Pause')} title="Pause" disabled={busy} onClick={() => void runPauseCall(() => pauseQueue(queue.id))} />
      )}
      {running && pause === 'pausing' && (
        <IconButton icon="undo" label={name('Cancel pause')} title="Cancel pause" disabled={busy} onClick={() => void runPauseCall(() => resumeQueue(queue.id))} />
      )}
      {running && pause === 'paused' && (
        <IconButton icon="play" label={name('Resume')} title="Resume" disabled={busy} onClick={() => void runPauseCall(() => resumeQueue(queue.id))} />
      )}
      {running && <IconButton icon="stop" label={name('Stop')} title="Stop" onClick={() => onStop(queue.id)} />}
      <IconButton icon="edit" label={name('Edit')} title="Edit" onClick={() => onEdit(queue.id)} />
      <IconButton icon="duplicate" label={name('Duplicate')} title="Duplicate" onClick={() => onDuplicate(queue.id)} />
      <IconButton icon="delete" label={name('Delete')} title="Delete" tone="destructive" disabled={running} onClick={() => onDelete(queue.id)} />
    </span>
  );
};
