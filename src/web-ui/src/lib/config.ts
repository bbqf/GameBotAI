type Subscriber<T> = (value: T) => void;

class Signal<T> {
  private value: T;
  private subs: Set<Subscriber<T>> = new Set();
  constructor(initial: T) { this.value = initial; }
  get(): T { return this.value; }
  set(v: T) { this.value = v; this.subs.forEach(s => s(v)); }
  subscribe(sub: Subscriber<T>): () => void { this.subs.add(sub); return () => this.subs.delete(sub); }
}

declare const __API_BASE_URL__: string | undefined;

const DEFAULT_BASE_URL = '';
const baseUrlLSKey = 'gamebot.baseUrl';
const envBaseUrl = (typeof __API_BASE_URL__ !== 'undefined' && __API_BASE_URL__)
  ? __API_BASE_URL__
  : (typeof process !== 'undefined' ? process.env?.VITE_API_BASE_URL ?? '' : '');

const initialBaseUrl = (() => {
  try {
    const ls = localStorage.getItem(baseUrlLSKey);
    if (ls && ls.length > 0) return ls;
  } catch { /* ignore storage errors */ }
  if (typeof envBaseUrl === 'string' && envBaseUrl.length > 0) return envBaseUrl;
  return DEFAULT_BASE_URL;
})();

export const baseUrl$ = new Signal<string>(initialBaseUrl);

export const getBaseUrl = () => baseUrl$.get();
export const setBaseUrl = (url: string) => {
  baseUrl$.set(url);
  try { localStorage.setItem(baseUrlLSKey, url); } catch { void 0; }
};