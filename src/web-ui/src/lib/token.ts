type Subscriber<T> = (value: T | null) => void;

class Signal<T> {
  private value: T | null;
  private subs: Set<Subscriber<T>> = new Set();
  constructor(initial: T | null) { this.value = initial; }
  get(): T | null { return this.value; }
  set(v: T | null) { this.value = v; this.subs.forEach(s => s(v)); }
  subscribe(sub: Subscriber<T>): () => void { this.subs.add(sub); return () => this.subs.delete(sub); }
}

const tokenLSKey = 'gamebot.token';
const rememberLSKey = 'gamebot.rememberToken';

const initialRemember = (() => {
  try { return localStorage.getItem(rememberLSKey) === 'true'; } catch { return false; }
})();
const initialToken = (() => {
  if (!initialRemember) return null;
  try { return localStorage.getItem(tokenLSKey); } catch { return null; }
})();

export const token$ = new Signal<string>(initialToken);

export const setToken = (t: string) => {
  token$.set(t);
  try {
    const remember = localStorage.getItem(rememberLSKey) === 'true';
    if (remember) localStorage.setItem(tokenLSKey, t);
  } catch { void 0; }
};

export const setRememberToken = (remember: boolean) => {
  try {
    localStorage.setItem(rememberLSKey, remember ? 'true' : 'false');
    if (!remember) localStorage.removeItem(tokenLSKey);
  } catch { void 0; }
};