import { useEffect, useState } from 'react';
import { baseUrl$ } from '../lib/config';
import { GameDto, listGames } from './games';

export type UseGamesState = {
  loading: boolean;
  data?: GameDto[];
  error?: string;
};

const cache = new Map<string, { data: GameDto[]; expiry: number }>();
const TTL_MS = 2 * 60 * 1000;

export const useGames = (): UseGamesState => {
  const [baseUrl, setBaseUrl] = useState(baseUrl$.get());
  const [state, setState] = useState<UseGamesState>({ loading: true });

  useEffect(() => baseUrl$.subscribe(setBaseUrl), []);

  useEffect(() => {
    let active = true;
    const cacheKey = baseUrl ?? '';
    const now = Date.now();
    const cached = cache.get(cacheKey);
    if (cached && cached.expiry > now) {
      setState({ loading: false, data: cached.data });
      return () => { active = false; };
    }

    setState({ loading: true });

    const load = async () => {
      try {
        const data = await listGames();
        cache.set(cacheKey, { data, expiry: Date.now() + TTL_MS });
        if (!active) return;
        setState({ loading: false, data });
      } catch (err: any) {
        if (!active) return;
        setState({ loading: false, error: err?.message ?? 'Failed to load games' });
      }
    };

    void load();
    return () => { active = false; };
  }, [baseUrl]);

  return state;
};
