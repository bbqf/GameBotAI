import { setRememberToken, setToken, token$ } from '../token';

describe('token helpers', () => {
  beforeEach(() => {
    localStorage.clear();
    token$.set(null);
  });

  it('persists token when remember flag is set', () => {
    setRememberToken(true);
    setToken('abc');

    expect(localStorage.getItem('gamebot.rememberToken')).toBe('true');
    expect(localStorage.getItem('gamebot.token')).toBe('abc');
    expect(token$.get()).toBe('abc');
  });

  it('does not persist token when remember is false', () => {
    setRememberToken(false);
    setToken('abc');

    expect(localStorage.getItem('gamebot.rememberToken')).toBe('false');
    expect(localStorage.getItem('gamebot.token')).toBeNull();
    expect(token$.get()).toBe('abc');
  });

  it('clears persisted token when disabling remember', () => {
    setRememberToken(true);
    setToken('persisted');

    setRememberToken(false);

    expect(localStorage.getItem('gamebot.token')).toBeNull();
  });
});
