import { NavigationAreaId } from '../types/navigation';

/** One screen of the phone UI. It is saved in a history entry. */
export type NavigationPosition = {
  area: NavigationAreaId;
  queueId: string | null;
};

const KNOWN_AREAS: NavigationAreaId[] = [
  'authoring', 'queues', 'monitoring', 'configuration', 'execution', 'execution-logs', 'notifications', 'update', 'gamepacks'
];

/** Build the address query for a position: `?area=<area>[&queue=<id>]`. */
export const buildAddress = (position: NavigationPosition): string => {
  const params = new URLSearchParams();
  params.set('area', position.area);
  if (position.queueId) params.set('queue', position.queueId);
  return `?${params.toString()}`;
};

/**
 * Read a position from the address. An unknown or missing area gives `fallbackArea`.
 * The queue id is kept only for the Queues area.
 */
export const parseAddress = (
  location: { search: string },
  fallbackArea: NavigationAreaId = 'queues'
): NavigationPosition => {
  const params = new URLSearchParams(location.search);
  const requested = params.get('area');
  const area = KNOWN_AREAS.find((a) => a === requested) ?? fallbackArea;
  const queue = params.get('queue');
  const keepQueue = area === 'queues' && (requested === null || requested === 'queues');
  return { area, queueId: keepQueue && queue ? queue : null };
};

type HistoryState = { idx?: number; area?: NavigationAreaId; queueId?: string | null } | null;

const readState = (): HistoryState => (window.history.state ?? null) as HistoryState;

const readIdx = (): number => {
  const idx = readState()?.idx;
  return typeof idx === 'number' ? idx : 0;
};

const currentPosition = (): NavigationPosition => parseAddress(window.location);

const samePosition = (a: NavigationPosition, b: NavigationPosition): boolean =>
  a.area === b.area && a.queueId === b.queueId;

const withQuery = (position: NavigationPosition): string => {
  const params = new URLSearchParams(window.location.search);
  params.set('area', position.area);
  if (position.queueId) params.set('queue', position.queueId);
  else params.delete('queue');
  return `${window.location.pathname}?${params.toString()}`;
};

/** Add a history entry for a new screen. Add none when the screen is already shown. */
export const pushScreen = (position: NavigationPosition): boolean => {
  if (samePosition(position, currentPosition())) return false;
  window.history.pushState({ idx: readIdx() + 1, ...position }, '', withQuery(position));
  return true;
};

/** Replace the current entry. Keep its index. */
export const replaceScreen = (position: NavigationPosition): void => {
  window.history.replaceState({ idx: readIdx(), ...position }, '', withQuery(position));
};

/** Mark the first entry of the session with `idx` 0. Keep an existing index (for example after a reload). */
export const initFirstEntry = (): void => {
  if (typeof readState()?.idx === 'number') return;
  window.history.replaceState({ ...(readState() ?? {}), idx: 0 }, '', window.location.href);
};

/** True when the current entry is the first entry of the session. */
export const isFirstEntry = (): boolean => readIdx() === 0;
