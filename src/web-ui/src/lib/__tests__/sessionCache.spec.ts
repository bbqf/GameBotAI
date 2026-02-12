import { sessionCache } from '../sessionCache';

describe('sessionCache', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('stores trimmed session ids', () => {
    sessionCache.set(' game-1 ', ' emu-1 ', ' sess-1 ');

    expect(localStorage.getItem('session:game-1:emu-1')).toBe('sess-1');
    expect(sessionCache.get('game-1', 'emu-1')).toBe('sess-1');
  });

  it('ignores set calls with missing values', () => {
    sessionCache.set('', 'emu-1', 'sess-1');
    sessionCache.set('game-1', '', 'sess-1');
    sessionCache.set('game-1', 'emu-1', '');

    expect(localStorage.length).toBe(0);
  });

  it('returns undefined when keys are missing', () => {
    expect(sessionCache.get('', 'emu-1')).toBeUndefined();
    expect(sessionCache.get('game-1', '')).toBeUndefined();
  });

  it('clears stored sessions', () => {
    sessionCache.set('game-1', 'emu-1', 'sess-1');

    sessionCache.clear('game-1', 'emu-1');

    expect(sessionCache.get('game-1', 'emu-1')).toBeUndefined();
  });
});
