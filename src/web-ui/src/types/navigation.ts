export type NavigationAreaId = 'authoring' | 'queues' | 'configuration' | 'execution' | 'execution-logs' | 'notifications';

export type NavigationArea = {
  id: NavigationAreaId;
  label: string;
  path: string;
  order: number;
};

export type NavigationState = {
  activeAreaId: NavigationAreaId;
  isCollapsed: boolean;
  focusTarget: string | null;
};

export const navigationAreas: NavigationArea[] = [
  { id: 'authoring', label: 'Authoring', path: '/', order: 0 },
  { id: 'queues', label: 'Queues', path: '/queues', order: 1 },
  { id: 'execution', label: 'Execution', path: '/execution', order: 2 },
  { id: 'execution-logs', label: 'Execution Logs', path: '/execution-logs', order: 3 },
  { id: 'notifications', label: 'Notifications', path: '/notifications', order: 4 },
  { id: 'configuration', label: 'Configuration', path: '/configuration', order: 5 }
];