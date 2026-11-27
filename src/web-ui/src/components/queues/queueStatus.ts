import type { QueueDto } from '../../services/queues';
import type { ChipState } from '../ui/StatusChip';

/** The chip state and text of a queue. A running queue with an operator pause shows Paused. */
export const queueChipState = (queue: Pick<QueueDto, 'status' | 'operatorPause'>): { state: ChipState; text: string } => {
  if (queue.status === 'Running') {
    return queue.operatorPause === 'paused' ? { state: 'paused', text: 'Paused' } : { state: 'running', text: 'Running' };
  }
  if (queue.status === 'Stopped') return { state: 'stopped', text: 'Stopped' };
  return { state: 'unknown', text: 'Unknown' };
};