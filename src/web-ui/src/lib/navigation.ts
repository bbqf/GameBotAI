import { AuthoringTab } from '../components/Nav';

const tabMap: Record<string, AuthoringTab> = {
  actions: 'Actions',
  commands: 'Commands',
  games: 'Games',
  sequences: 'Sequences',
  triggers: 'Triggers'
};

export const normalizeTab = (value: string | null): AuthoringTab | undefined => {
  if (!value) return undefined;
  return tabMap[value.toLowerCase()] ?? undefined;
};

export const buildUnifiedUrl = (tab: AuthoringTab, opts?: { create?: boolean; id?: string }): string => {
  const params = new URLSearchParams();
  params.set('tab', tab);
  if (opts?.create) params.set('create', tab.toLowerCase());
  if (opts?.id) params.set('id', opts.id);
  return `/?${params.toString()}`;
};

export const navigateToUnified = (tab: AuthoringTab, opts?: { create?: boolean; id?: string; newTab?: boolean }) => {
  const url = buildUnifiedUrl(tab, opts);
  if (opts?.newTab) {
    window.open(url, '_blank');
  } else {
    window.location.assign(url);
  }
};
