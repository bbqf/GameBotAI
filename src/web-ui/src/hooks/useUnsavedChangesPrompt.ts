import { useEffect } from 'react';

export const useUnsavedChangesPrompt = (dirty: boolean) => {
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (!dirty) return;
      e.preventDefault();
      e.returnValue = '';
      return '';
    };
    window.addEventListener('beforeunload', handler);
    return () => {
      window.removeEventListener('beforeunload', handler);
    };
  }, [dirty]);

  const confirmNavigate = () => {
    if (!dirty) return true;
    return window.confirm('You have unsaved changes. Leave this page?');
  };

  return { confirmNavigate };
};
