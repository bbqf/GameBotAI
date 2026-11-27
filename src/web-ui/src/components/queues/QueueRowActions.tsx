import React, { useRef, useState } from 'react';
import { OperatorPauseState, pauseQueue, QueueDto, resumeQueue } from '../../services/queues';
import './QueueRowActions.css';
import { IconButton, RowActionMenu, type RowAction, type RowActions } from '../ui';

export type QueueRowQueue = Pick<QueueDto, 'id' | 'name' | 'status' | 'operatorPause'>;

export type QueueRowHandlers = {
  onStart: (id: string) => void;
  onStop: (id: string) => void;
  onEdit: (id: string) => void;
  onDuplicate: (id: string) => void;
  onDelete: (id: string) => void;
};

export type QueuePauseCallOptions = {
  onStart: (id: string) => void;
  /** Called after a pause, a resume, or a cancel, so that the page can read the list again. */
  onChanged?: () => void;
  /** Called with the text of a refused call. */
  onError?: (message: string) => void;
};

export type QueueRowActionsProps = QueueRowHandlers & {
  queue: QueueRowQueue;
  onChanged?: () => void;
  onError?: (message: string) => void;
};

const errorText = (err: unknown): string => (err instanceof Error && err.message ? err.message : 'The request failed.');

/**
 * The first action of a queue row: Start for a stopped queue, and Pause, Cancel pause, or Resume for a running queue.
 * A pause call that runs turns the action off. A second call cannot start while one runs.
 */
export const useQueuePauseCalls = (queue: QueueRowQueue, { onStart, onChanged, onError }: QueuePauseCallOptions): { busy: boolean; slot1Action: RowAction } => {
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

  let slot1Action: RowAction;
  if (!running) {
    slot1Action = { id: 'start', label: 'Start', ariaLabel: name('Start'), icon: 'play', onSelect: () => onStart(queue.id) };
  } else if (pause === 'none') {
    slot1Action = { id: 'pause', label: 'Pause', ariaLabel: name('Pause'), icon: 'pause', disabled: busy, onSelect: () => void runPauseCall(() => pauseQueue(queue.id)) };
  } else if (pause === 'pausing') {
    slot1Action = { id: 'cancel-pause', label: 'Cancel pause', ariaLabel: name('Cancel pause'), icon: 'undo', disabled: busy, onSelect: () => void runPauseCall(() => resumeQueue(queue.id)) };
  } else {
    slot1Action = { id: 'resume', label: 'Resume', ariaLabel: name('Resume'), icon: 'play', disabled: busy, onSelect: () => void runPauseCall(() => resumeQueue(queue.id)) };
  }
  return { busy, slot1Action };
};

/**
 * The actions of one row of the Queues list: three icons in fixed places and a menu. Stop stays in its place and is off
 * while the queue is stopped. Delete is off while the queue runs.
 */
export const queueRowActions = (queue: QueueRowQueue, handlers: QueueRowHandlers, pause: { slot1Action: RowAction }): RowActions => {
  const running = queue.status === 'Running';
  const name = (action: string) => `${action} ${queue.name}`;
  return {
    visible: [
      pause.slot1Action,
      { id: 'stop', label: 'Stop', ariaLabel: name('Stop'), icon: 'stop', disabled: !running, onSelect: () => handlers.onStop(queue.id) },
      { id: 'edit', label: 'Edit', ariaLabel: name('Edit'), icon: 'edit', onSelect: () => handlers.onEdit(queue.id) },
    ],
    menu: [
      { id: 'duplicate', label: 'Duplicate', ariaLabel: name('Duplicate'), icon: 'duplicate', onSelect: () => handlers.onDuplicate(queue.id) },
      { id: 'delete', label: 'Delete', ariaLabel: name('Delete'), icon: 'delete', tone: 'danger', disabled: running, onSelect: () => handlers.onDelete(queue.id) },
    ],
  };
};

/** The icons of one row of the Queues list and its menu. Each icon has the name "<Action> <queue name>". */
export const QueueRowActions: React.FC<QueueRowActionsProps> = ({ queue, onStart, onStop, onEdit, onDuplicate, onDelete, onChanged, onError }) => {
  const pause = useQueuePauseCalls(queue, { onStart, onChanged, onError });
  const set = queueRowActions(queue, { onStart, onStop, onEdit, onDuplicate, onDelete }, pause);
  return (
    <span className="queue-row-actions" role="group" aria-label={`Actions for ${queue.name}`}>
      {set.visible.map((a) => (
        <IconButton key={a.id} icon={a.icon} label={a.ariaLabel ?? a.label} title={a.label} disabled={a.disabled} onClick={a.onSelect} />
      ))}
      <RowActionMenu label={`More actions for ${queue.name}`} actions={set.menu ?? []} />
    </span>
  );
};