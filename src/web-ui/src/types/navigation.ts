export type NavigationAreaId = 'authoring' | 'queues' | 'monitoring' | 'configuration' | 'execution' | 'execution-logs' | 'notifications' | 'update' | 'gamepacks';

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
};

export type NavigationState = {
  activeAreaId: NavigationAreaId;
  isCollapsed: boolean;
  focusTarget: string | null;
};

export const navigationAreas: NavigationArea[] = [
  { id: 'queues', label: 'Queues', path: '/queues', order: 0, phone: 'reduced' },
  { id: 'monitoring', label: 'Monitoring', path: '/monitoring', order: 1, phone: 'full' },
  { id: 'authoring', label: 'Authoring', path: '/', order: 2, phone: 'desktop-only' },
  { id: 'execution', label: 'Execution', path: '/execution', order: 3, phone: 'hidden' },
  { id: 'execution-logs', label: 'Execution Logs', path: '/execution-logs', order: 4, phone: 'desktop-only' },
  { id: 'configuration', label: 'Configuration', path: '/configuration', order: 5, phone: 'full' },
  { id: 'update', label: 'Update', path: '/update', order: 6, phone: 'full' },
  { id: 'gamepacks', label: 'Gamepacks', path: '/gamepacks', order: 7, phone: 'full' },
  { id: 'notifications', label: 'Notifications', path: '/notifications', order: 8, phone: 'full' }
];
/** True when the phone UI lists the area. */
export const isPhoneVisible = (area: Pick<NavigationArea, 'phone'>): boolean => area.phone !== 'hidden';
