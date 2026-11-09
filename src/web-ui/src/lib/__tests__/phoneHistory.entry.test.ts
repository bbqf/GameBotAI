import { buildAddress, initFirstEntry, parseAddress, pushScreen, replaceScreen } from '../phoneHistory';

describe('phoneHistory entry position (feature 185)', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', '/?area=queues');
  });

  it('builds an address with an entry', () => {
    expect(buildAddress({ area: 'queues', queueId: 'q1', entryId: 'e1' })).toBe('?area=queues&queue=q1&entry=e1');
    expect(buildAddress({ area: 'queues', queueId: 'q1', entryId: null })).toBe('?area=queues&queue=q1');
    expect(buildAddress({ area: 'queues', queueId: 'q1' })).toBe('?area=queues&queue=q1');
  });

  it('drops the entry when there is no queue', () => {
    expect(buildAddress({ area: 'queues', queueId: null, entryId: 'e1' })).toBe('?area=queues');
  });

  it('parses an entry only for the Queues area with a queue', () => {
    expect(parseAddress({ search: '?area=queues&queue=q1&entry=e1' })).toEqual({ area: 'queues', queueId: 'q1', entryId: 'e1' });
    expect(parseAddress({ search: '?area=queues&entry=e1' })).toEqual({ area: 'queues', queueId: null });
    expect(parseAddress({ search: '?area=update&queue=q1&entry=e1' })).toEqual({ area: 'update', queueId: null });
    expect(parseAddress({ search: '?area=queues&queue=q1' })).toEqual({ area: 'queues', queueId: 'q1' });
  });

  it('adds one history entry for the entry page and none for the same position', () => {
    initFirstEntry();
    pushScreen({ area: 'queues', queueId: 'q1', entryId: null });
    const length = window.history.length;

    expect(pushScreen({ area: 'queues', queueId: 'q1', entryId: 'e1' })).toBe(true);
    expect(window.location.search).toBe('?area=queues&queue=q1&entry=e1');
    expect(window.history.state.idx).toBe(2);
    expect(window.history.state.entryId).toBe('e1');
    expect(window.history.length).toBe(length + 1);

    expect(pushScreen({ area: 'queues', queueId: 'q1', entryId: 'e1' })).toBe(false);
    expect(window.history.length).toBe(length + 1);
  });

  it('removes the entry from the address when the position has none', () => {
    initFirstEntry();
    pushScreen({ area: 'queues', queueId: 'q1', entryId: 'e1' });

    pushScreen({ area: 'queues', queueId: 'q1', entryId: null });

    expect(window.location.search).toBe('?area=queues&queue=q1');
  });

  it('replaces the entry and keeps idx', () => {
    initFirstEntry();
    pushScreen({ area: 'queues', queueId: 'q1', entryId: 'e1' });

    replaceScreen({ area: 'queues', queueId: 'q1', entryId: 'e2' });

    expect(window.history.state.idx).toBe(1);
    expect(window.location.search).toBe('?area=queues&queue=q1&entry=e2');
  });

  it('treats an old caller with no entryId as the same position as a null entry', () => {
    initFirstEntry();
    expect(pushScreen({ area: 'queues', queueId: null })).toBe(false);
  });
});
