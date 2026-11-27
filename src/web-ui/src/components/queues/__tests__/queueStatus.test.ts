import { queueChipState } from '../queueStatus';

describe('queueChipState', () => {
  it.each([
    [{ status: 'Running', operatorPause: 'paused' }, { state: 'paused', text: 'Paused' }],
    [{ status: 'Running', operatorPause: 'none' }, { state: 'running', text: 'Running' }],
    [{ status: 'Running', operatorPause: 'pausing' }, { state: 'running', text: 'Running' }],
    [{ status: 'Running' }, { state: 'running', text: 'Running' }],
    [{ status: 'Stopped' }, { state: 'stopped', text: 'Stopped' }],
    [{ status: 'Other' }, { state: 'unknown', text: 'Unknown' }],
  ])('maps %j', (queue, expected) => {
    expect(queueChipState(queue as any)).toEqual(expected);
  });
});