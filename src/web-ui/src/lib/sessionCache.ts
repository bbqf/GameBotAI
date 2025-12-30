const makeKey = (gameId: string, adbSerial: string) => `session:${gameId}:${adbSerial}`;

const safe = (value?: string) => (typeof value === 'string' ? value.trim() : '');

export const sessionCache = {
  set(gameId: string, adbSerial: string, sessionId: string) {
    const g = safe(gameId);
    const d = safe(adbSerial);
    const s = safe(sessionId);
    if (!g || !d || !s) return;
    localStorage.setItem(makeKey(g, d), s);
  },
  get(gameId: string, adbSerial: string): string | undefined {
    const g = safe(gameId);
    const d = safe(adbSerial);
    if (!g || !d) return undefined;
    const raw = localStorage.getItem(makeKey(g, d));
    return raw?.trim() || undefined;
  },
  clear(gameId: string, adbSerial: string) {
    const g = safe(gameId);
    const d = safe(adbSerial);
    if (!g || !d) return;
    localStorage.removeItem(makeKey(g, d));
  }
};
