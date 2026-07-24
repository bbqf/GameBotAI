import { useEffect, useRef } from 'react';

/**
 * Returns a ref to attach to an editor panel. Whenever `openKey` transitions to
 * a new truthy value, the panel is scrolled into view so the user isn't left
 * staring at the list they clicked from while the form sits far below the fold.
 *
 * `openKey` is typically the editing id (a string) or a sentinel such as
 * `'create'` for a create form; changing it (e.g. re-selecting a different row)
 * re-triggers the scroll. Falsy values (nothing open) do nothing.
 */
export const useScrollIntoViewOnOpen = <T extends HTMLElement = HTMLElement>(
  openKey: string | boolean | undefined | null,
) => {
  const ref = useRef<T>(null);
  useEffect(() => {
    if (!openKey) return;
    // Defer to the next frame so the panel has mounted and laid out before we scroll.
    const raf = requestAnimationFrame(() => {
      // jsdom (tests) has no layout engine and throws "Not implemented" from
      // scrollIntoView — swallow so the effect stays a no-op there.
      try {
        ref.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      } catch {
        /* no-op: environment without scroll support */
      }
    });
    return () => cancelAnimationFrame(raf);
  }, [openKey]);
  return ref;
};
