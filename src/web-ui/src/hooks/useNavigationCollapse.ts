import { useEffect, useState } from 'react';

const DEFAULT_BREAKPOINT = 768;

export const useNavigationCollapse = (breakpoint: number = DEFAULT_BREAKPOINT) => {
  const [isCollapsed, setIsCollapsed] = useState<boolean>(false);

  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) {
      setIsCollapsed(false);
      return;
    }
    const query = `(max-width: ${breakpoint}px)`;
    const media = window.matchMedia(query);
    const handleChange = (e: MediaQueryListEvent | MediaQueryList) => setIsCollapsed(e.matches);
    handleChange(media);
    media.addEventListener('change', handleChange as any);
    return () => media.removeEventListener('change', handleChange as any);
  }, [breakpoint]);

  return { isCollapsed, breakpoint };
};