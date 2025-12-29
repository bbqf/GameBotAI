export type NavigationAreaId = 'authoring' | 'configuration' | 'execution';

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
  { id: 'execution', label: 'Execution', path: '/execution', order: 1 },
  { id: 'configuration', label: 'Configuration', path: '/configuration', order: 2 }
];