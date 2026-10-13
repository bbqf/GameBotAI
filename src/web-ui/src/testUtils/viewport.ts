import { PHONE_MEDIA_QUERY } from '../hooks/useLayoutMode';

type Listener = (e: MediaQueryListEvent) => void;

const parseLimit = (query: string, feature: 'width' | 'height'): number | undefined => {
  const match = new RegExp(`\\(max-${feature}:\\s*(\\d+)px\\)`).exec(query);
  return match ? Number(match[1]) : undefined;
};

const queryMatches = (query: string, width: number, height: number): boolean => {
  const maxWidth = parseLimit(query, 'width');
  const maxHeight = parseLimit(query, 'height');
  return (maxWidth !== undefined && width <= maxWidth) || (maxHeight !== undefined && height <= maxHeight);
};

export type ViewportMock = {
  /** Changes the size and fires `change` events on queries whose result changed. */
  setViewport: (width: number, height: number) => void;
  restore: () => void;
};

/** Installs a `window.matchMedia` mock for a given size. Only `max-width` and `max-height` queries are known. */
export const mockViewport = (width: number, height: number): ViewportMock => {
  const original = window.matchMedia;
  let current = { width, height };
  const entries: Array<{ query: string; matches: boolean; listeners: Listener[]; list: MediaQueryList }> = [];

  window.matchMedia = ((query: string) => {
    const entry = {
      query,
      matches: queryMatches(query, current.width, current.height),
      listeners: [] as Listener[],
      list: undefined as unknown as MediaQueryList
    };
    entry.list = {
      get matches() { return entry.matches; },
      media: query,
      onchange: null,
      addEventListener: (_: string, cb: Listener) => { entry.listeners.push(cb); },
      removeEventListener: (_: string, cb: Listener) => {
        const idx = entry.listeners.indexOf(cb);
        if (idx >= 0) entry.listeners.splice(idx, 1);
      },
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false
    } as unknown as MediaQueryList;
    entries.push(entry);
    return entry.list;
  }) as typeof window.matchMedia;

  return {
    setViewport: (w, h) => {
      current = { width: w, height: h };
      for (const entry of entries) {
        const next = queryMatches(entry.query, w, h);
        if (next === entry.matches) continue;
        entry.matches = next;
        for (const cb of [...entry.listeners]) cb({ matches: next, media: entry.query } as MediaQueryListEvent);
      }
    },
    restore: () => { window.matchMedia = original; }
  };
};

/** Shortcut: a 360 x 640 phone. */
export const mockPhoneViewport = () => mockViewport(360, 640);

/** Shortcut: a 1280 x 800 desktop. */
export const mockDesktopViewport = () => mockViewport(1280, 800);

export { PHONE_MEDIA_QUERY };
