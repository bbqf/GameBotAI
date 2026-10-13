import { useEffect, useState } from 'react';

export type LayoutMode = 'phone' | 'desktop';

/** Phone layout applies on narrow screens and on short screens (phone in landscape). */
export const PHONE_MEDIA_QUERY = '(max-width: 768px), (max-height: 500px)';

const readMode = (): LayoutMode => {
  if (typeof window === 'undefined' || !window.matchMedia) return 'desktop';
  return window.matchMedia(PHONE_MEDIA_QUERY).matches ? 'phone' : 'desktop';
};

/** Returns the layout mode. The value changes with the window size, without a reload. */
export const useLayoutMode = (): LayoutMode => {
  const [mode, setMode] = useState<LayoutMode>(readMode);

  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return;
    const media = window.matchMedia(PHONE_MEDIA_QUERY);
    const handleChange = (e: MediaQueryListEvent | MediaQueryList) => setMode(e.matches ? 'phone' : 'desktop');
    handleChange(media);
    media.addEventListener('change', handleChange as (e: MediaQueryListEvent) => void);
    return () => media.removeEventListener('change', handleChange as (e: MediaQueryListEvent) => void);
  }, []);

  return mode;
};
