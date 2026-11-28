import { useCallback, useRef, useState } from 'react';
import { ApiError } from '../../lib/api';

export type DeleteTarget = { id: string; name: string };
export type DeleteReferencesMap = Record<string, Array<{ id: string; name: string }>>;
export type DeleteConflict = { message: string; references?: DeleteReferencesMap };

type UseDeleteFlowOptions = {
  /** The kind of item. It is used in the default texts. */
  kind: 'game' | 'command' | 'sequence';
  remove: (id: string) => Promise<void>;
  onDeleted: (target: DeleteTarget) => void;
  onError: (message: string) => void;
};

/** The state of a delete dialog: the target, the busy flag, and the 409 conflict with its references. */
export const useDeleteFlow = ({ kind, remove, onDeleted, onError }: UseDeleteFlowOptions) => {
  const [target, setTarget] = useState<DeleteTarget | undefined>(undefined);
  const [conflict, setConflict] = useState<DeleteConflict | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);

  const open = useCallback((next: DeleteTarget) => {
    setTarget(next);
    setConflict(undefined);
  }, []);

  const cancel = useCallback(() => {
    setTarget(undefined);
    setConflict(undefined);
  }, []);

  const confirm = useCallback(async () => {
    if (!target || busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    try {
      await remove(target.id);
      setTarget(undefined);
      setConflict(undefined);
      onDeleted(target);
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        setConflict({
          message: err.message || `Cannot delete: the ${kind} is in use. Remove the links first.`,
          references: err.references,
        });
      } else {
        setTarget(undefined);
        setConflict(undefined);
        onError(err?.message ?? `Failed to delete ${kind}`);
      }
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }, [target, remove, onDeleted, onError, kind]);

  return { target, conflict, busy, open, cancel, confirm };
};
