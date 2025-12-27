import { useEffect, useState } from 'react';
import { baseUrl$ } from '../lib/config';
import { ActionTypeCatalog } from '../types/actions';
import { getActionTypes } from './actionsApi';

const TTL_MS = 5 * 60 * 1000;

type CacheEntry = {
  data: ActionTypeCatalog;
  etag?: string;
  expiry: number;
};

const cache = new Map<string, CacheEntry>();

export type UseActionTypesState = {
  loading: boolean;
  data?: ActionTypeCatalog;
  error?: string;
};

export const useActionTypes = (): UseActionTypesState => {
  const [baseUrl, setBaseUrl] = useState(baseUrl$.get());
  const [state, setState] = useState<UseActionTypesState>({ loading: true });

  useEffect(() => baseUrl$.subscribe(setBaseUrl), []);

  useEffect(() => {
    let active = true;
    const cacheKey = baseUrl ?? '';
    const now = Date.now();
    const cached = cache.get(cacheKey);

    const apply = (next: UseActionTypesState) => {
      if (!active) return;
      setState(next);
    };

    const saveCache = (entry: CacheEntry) => {
      cache.set(cacheKey, entry);
      apply({ loading: false, data: entry.data });
    };

    if (cached && cached.expiry > now) {
      apply({ loading: false, data: cached.data });
      return () => { active = false; };
    }

    apply({ loading: true });

    const load = async () => {
      const etag = cached?.etag;
      const fetchOnce = async (useEtag: boolean) => {
        const result = await getActionTypes(useEtag ? etag : undefined);
        if (result.notModified) {
          if (cached) {
            saveCache({ data: cached.data, etag: cached.etag, expiry: Date.now() + TTL_MS });
            return true;
          }
          return false;
        }
        if (!result.catalog) return false;
        saveCache({ data: result.catalog, etag: result.etag, expiry: Date.now() + TTL_MS });
        return true;
      };

      try {
        const ok = await fetchOnce(true);
        if (!ok) {
          const retried = await fetchOnce(false);
          if (!retried) throw new Error('Failed to load action types');
        }
      } catch (err: any) {
        apply({ loading: false, error: err?.message ?? 'Failed to load action types' });
      }
    };

    void load();
    return () => { active = false; };
  }, [baseUrl]);

  return state;
};
