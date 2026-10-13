import {
  buildAddress, initFirstEntry, isFirstEntry, parseAddress, pushScreen, replaceScreen
} from '../phoneHistory';

describe('phoneHistory', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', '/?area=queues');
  });

  it('builds addresses', () => {
    expect(buildAddress({ area: 'queues', queueId: null })).toBe('?area=queues');
    expect(buildAddress({ area: 'queues', queueId: 'q1' })).toBe('?area=queues&queue=q1');
  });

  it('parses addresses and falls back for unknown values', () => {
    expect(parseAddress({ search: '?area=queues&queue=q1' })).toEqual({ area: 'queues', queueId: 'q1' });
    expect(parseAddress({ search: '?area=nope&queue=q1' })).toEqual({ area: 'queues', queueId: null });
    expect(parseAddress({ search: '' })).toEqual({ area: 'queues', queueId: null });
    expect(parseAddress({ search: '?area=update&queue=q1' })).toEqual({ area: 'update', queueId: null });
  });

  it('increases idx on push and adds no entry for the same position', () => {
    initFirstEntry();
    const length = window.history.length;
    expect(pushScreen({ area: 'queues', queueId: null })).toBe(false);
    expect(window.history.length).toBe(length);
    expect(pushScreen({ area: 'queues', queueId: 'q1' })).toBe(true);
    expect(window.history.state.idx).toBe(1);
    expect(window.location.search).toBe('?area=queues&queue=q1');
    pushScreen({ area: 'notifications', queueId: null });
    expect(window.history.state.idx).toBe(2);
  });

  it('keeps idx on replace', () => {
    initFirstEntry();
    pushScreen({ area: 'notifications', queueId: null });
    replaceScreen({ area: 'queues', queueId: null });
    expect(window.history.state.idx).toBe(1);
    expect(window.location.search).toBe('?area=queues');
  });

  it('knows the first entry, also after a simulated reload', () => {
    initFirstEntry();
    expect(isFirstEntry()).toBe(true);
    pushScreen({ area: 'update', queueId: null });
    expect(isFirstEntry()).toBe(false);
    // A reload keeps history.state, so a second init must not reset idx.
    initFirstEntry();
    expect(window.history.state.idx).toBe(1);
    expect(isFirstEntry()).toBe(false);
  });
});
