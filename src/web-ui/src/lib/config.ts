type Subscriber<T> = (value: T) => void;

class Signal<T> {
  private value: T;
  private subs: Set<Subscriber<T>> = new Set();
  constructor(initial: T) { this.value = initial; }
  get(): T { return this.value; }
  set(v: T) { this.value = v; this.subs.forEach(s => s(v)); }
  subscribe(sub: Subscriber<T>): () => void { this.subs.add(sub); return () => this.subs.delete(sub); }
}

const DEFAULT_BASE_URL = '';
const baseUrlLSKey = 'gamebot.baseUrl';

const initialBaseUrl = (() => {
  try {
    const ls = localStorage.getItem(baseUrlLSKey);
    return ls ?? DEFAULT_BASE_URL;
  } catch { return DEFAULT_BASE_URL; }
})();

export const baseUrl$ = new Signal<string>(initialBaseUrl);

export const getBaseUrl = () => baseUrl$.get();
export const setBaseUrl = (url: string) => {
  baseUrl$.set(url);
  try { localStorage.setItem(baseUrlLSKey, url); } catch { void 0; }
};