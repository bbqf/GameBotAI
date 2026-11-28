import type { IconName } from '../components/ui/icons';

export type NavigationAreaId = 'authoring' | 'queues' | 'monitoring' | 'configuration' | 'execution' | 'execution-logs' | 'notifications' | 'update' | 'gamepacks' | 'more';

/**
 * How much of an area works on a phone.
 * - `full`: same functions as on the desktop, in a phone layout.
 * - `reduced`: a subset of the functions.
 * - `desktop-only`: the area shows a notice and a way back.
 * - `hidden`: the area is not part of the phone UI. No list shows it.
 */
export type PhoneSupportLevel = 'full' | 'reduced' | 'desktop-only' | 'hidden';

export type NavigationArea = {
  id: NavigationAreaId;
  label: string;
  path: string;
  order: number;
  phone: PhoneSupportLevel;
  icon: IconName;
  /** `main`: a top tab. `settings`: a page under the Settings tab (desktop) or the More page (phone). */
  group: 'main' | 'settings';
};

export type NavigationState = {
  activeAreaId: NavigationAreaId;
  isCollapsed: boolean;
  focusTarget: string | null;
};

export const navigationAreas: NavigationArea[] = [
  { id: 'queues', label: 'Queues', path: '/queues', order: 0, phone: 'reduced', icon: 'queue', group: 'main' },
  { id: 'monitoring', label: 'Monitor', path: '/monitoring', order: 1, phone: 'full', icon: 'monitor', group: 'main' },
  { id: 'notifications', label: 'Alerts', path: '/notifications', order: 2, phone: 'full', icon: 'alerts', group: 'main' },
  { id: 'authoring', label: 'Authoring', path: '/', order: 3, phone: 'desktop-only', icon: 'authoring', group: 'main' },
  { id: 'execution-logs', label: 'Execution logs', path: '/execution-logs', order: 0, phone: 'desktop-only', icon: 'logs', group: 'settings' },
  { id: 'execution', label: 'Execution', path: '/execution', order: 1, phone: 'hidden', icon: 'execution', group: 'settings' },
  { id: 'configuration', label: 'Configuration', path: '/configuration', order: 2, phone: 'full', icon: 'settings', group: 'settings' },
  { id: 'gamepacks', label: 'Gamepacks', path: '/gamepacks', order: 3, phone: 'full', icon: 'gamepacks', group: 'settings' },
  { id: 'update', label: 'Update', path: '/update', order: 4, phone: 'full', icon: 'update', group: 'settings' },
  // The More page exists on the phone only. The desktop shows the top tabs and the sub-tabs instead.
  { id: 'more', label: 'More', path: '/more', order: 5, phone: 'full', icon: 'more', group: 'settings' }
];

/** True when the phone UI lists the area. */
export const isPhoneVisible = (area: Pick<NavigationArea, 'phone'>): boolean => area.phone !== 'hidden';


/** The 5 top tabs of the desktop app bar. */
export type MainTabId = 'queues' | 'monitoring' | 'notifications' | 'authoring' | 'settings';

/** The 4 tabs of the phone bottom bar. */
export type PhoneTabId = 'queues' | 'monitoring' | 'notifications' | 'more';

export type AuthoringTab = 'Commands' | 'Games' | 'Sequences' | 'Images' | 'Backup & Restore';

export const AUTHORING_TABS: AuthoringTab[] = ['Commands', 'Games', 'Sequences', 'Images', 'Backup & Restore'];

export const MAIN_TABS: { id: MainTabId; label: string; icon: IconName }[] = [
  { id: 'queues', label: 'Queues', icon: 'queue' },
  { id: 'monitoring', label: 'Monitor', icon: 'monitor' },
  { id: 'notifications', label: 'Alerts', icon: 'alerts' },
  { id: 'authoring', label: 'Authoring', icon: 'authoring' },
  { id: 'settings', label: 'Settings', icon: 'settings' }
];

export const PHONE_TABS: { id: PhoneTabId; label: string; icon: IconName }[] = [
  { id: 'queues', label: 'Queues', icon: 'queue' },
  { id: 'monitoring', label: 'Monitor', icon: 'monitor' },
  { id: 'notifications', label: 'Alerts', icon: 'alerts' },
  { id: 'more', label: 'More', icon: 'more' }
];

/** The pages under the Settings tab, in order. */
export const SETTINGS_SUB_TABS: NavigationAreaId[] = ['execution-logs', 'execution', 'configuration', 'gamepacks', 'update'];

/** A click on the Settings tab opens this page. */
export const SETTINGS_FIRST: NavigationAreaId = 'execution-logs';

/** The rows of the More page that open a page. */
export const MORE_ROWS: NavigationAreaId[] = ['configuration', 'gamepacks', 'update'];

/** The dimmed rows of the More page (the areas that need a computer). */
export const MORE_DESKTOP_ONLY_ROWS: NavigationAreaId[] = ['execution-logs', 'authoring'];

/** The desktop top tab of an area. The Settings pages and the More page belong to Settings. */
export const mainTabOf = (area: NavigationAreaId): MainTabId => {
  const def = navigationAreas.find((a) => a.id === area);
  if (def?.group === 'settings') return 'settings';
  return area as MainTabId;
};

/** The phone bottom tab of an area. Each area that is not a bottom tab belongs to More. */
export const phoneTabOf = (area: NavigationAreaId): PhoneTabId => (
  area === 'queues' || area === 'monitoring' || area === 'notifications' ? area : 'more'
);

/** True when the phone page of the area shows a back arrow to the More page. */
export const hasMoreBack = (area: NavigationAreaId): boolean => (
  area === 'configuration' || area === 'gamepacks' || area === 'update' || area === 'execution-logs' || area === 'authoring'
);

/** The area that the desktop shows for a requested area. The desktop has no More page. */
export const desktopArea = (area: NavigationAreaId): NavigationAreaId => (area === 'more' ? SETTINGS_FIRST : area);
