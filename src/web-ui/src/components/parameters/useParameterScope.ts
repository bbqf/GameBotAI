import { useCallback, useEffect, useState } from 'react';
import { getJson } from '../../lib/api';
import type { ParameterScopeEntry, ParameterScopeResponse, ParameterStepCallee } from './types';

export type UseParameterScopeResult = {
  /** Names the insert-parameter picker may offer. Empty until loaded. */
  entries: ParameterScopeEntry[];
  /** Declarations of each command a sequence's steps invoke, keyed by step id. */
  stepCallees: ParameterStepCallee[];
  loading: boolean;
  error?: string;
  /** Re-fetch, e.g. after declarations were saved. */
  refresh: () => void;
};

/**
 * Loads the in-scope parameter names for a command or sequence (feature 078).
 *
 * The scope is served by the backend rather than derived here on purpose: resolution precedence and
 * the built-in catalogue then have exactly one implementation, so the picker can never offer a name
 * the runtime would refuse to resolve.
 *
 * @param kind Which entity the scope belongs to.
 * @param id Entity id; the hook is inert while this is undefined (e.g. an unsaved draft).
 */
export const useParameterScope = (
  kind: 'commands' | 'sequences',
  id: string | undefined,
): UseParameterScopeResult => {
  const [entries, setEntries] = useState<ParameterScopeEntry[]>([]);
  const [stepCallees, setStepCallees] = useState<ParameterStepCallee[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const [nonce, setNonce] = useState(0);

  const refresh = useCallback(() => setNonce((n) => n + 1), []);

  useEffect(() => {
    if (!id) {
      setEntries([]);
      setStepCallees([]);
      setError(undefined);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(undefined);

    getJson<ParameterScopeResponse>(`/api/${kind}/${id}/parameter-scope`)
      .then((response) => {
        if (cancelled) return;
        setEntries(response.entries ?? []);
        setStepCallees(response.stepCallees ?? []);
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        // A missing scope must not block editing — the picker simply has nothing to offer, and the
        // backend still validates on save.
        setEntries([]);
        setStepCallees([]);
        setError(e instanceof Error ? e.message : 'Could not load parameters in scope.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [kind, id, nonce]);

  return { entries, stepCallees, loading, error, refresh };
};
