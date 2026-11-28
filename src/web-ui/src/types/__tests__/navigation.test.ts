import {
  AUTHORING_TABS,
  MAIN_TABS,
  MORE_DESKTOP_ONLY_ROWS,
  MORE_ROWS,
  NavigationAreaId,
  PHONE_TABS,
  SETTINGS_FIRST,
  SETTINGS_SUB_TABS,
  desktopArea,
  hasMoreBack,
  isPhoneVisible,
  mainTabOf,
  navigationAreas,
  phoneTabOf
} from '../navigation';

const level = (id: string) => navigationAreas.find((a) => a.id === id)?.phone;

describe('navigationAreas phone support level', () => {
  it('gives each area a level', () => {
    for (const area of navigationAreas) {
      expect(['full', 'reduced', 'desktop-only', 'hidden']).toContain(area.phone);
    }
  });

  it('marks authoring and execution-logs as desktop-only', () => {
    expect(level('authoring')).toBe('desktop-only');
    expect(level('execution-logs')).toBe('desktop-only');
  });

  it('hides execution on the phone and keeps it in the list', () => {
    expect(level('execution')).toBe('hidden');
    expect(navigationAreas.some((a) => a.id === 'execution')).toBe(true);
  });

  it('adds Monitoring as a full phone area, with the label Monitor (features 141 and 205)', () => {
    expect(level('monitoring')).toBe('full');
    const monitoring = navigationAreas.find((a) => a.id === 'monitoring');
    expect(monitoring).toMatchObject({ label: 'Monitor', path: '/monitoring', order: 1, group: 'main', icon: 'monitor' });
  });

  it('has 10 areas with the agreed labels, icons, and groups', () => {
    expect(navigationAreas).toHaveLength(10);
    expect(navigationAreas.map((a) => [a.id, a.label, a.icon, a.group])).toEqual([
      ['queues', 'Queues', 'queue', 'main'],
      ['monitoring', 'Monitor', 'monitor', 'main'],
      ['notifications', 'Alerts', 'alerts', 'main'],
      ['authoring', 'Authoring', 'authoring', 'main'],
      ['execution-logs', 'Execution logs', 'logs', 'settings'],
      ['execution', 'Execution', 'execution', 'settings'],
      ['configuration', 'Configuration', 'settings', 'settings'],
      ['gamepacks', 'Gamepacks', 'gamepacks', 'settings'],
      ['update', 'Update', 'update', 'settings'],
      ['more', 'More', 'more', 'settings']
    ]);
  });

  it('gives the More area the path /more and a full phone level', () => {
    expect(navigationAreas.find((a) => a.id === 'more')).toMatchObject({ path: '/more', phone: 'full' });
  });

  it('keeps the other levels', () => {
    expect(level('queues')).toBe('reduced');
    expect(level('notifications')).toBe('full');
    expect(level('configuration')).toBe('full');
    expect(level('update')).toBe('full');
    expect(level('gamepacks')).toBe('full');
  });

  it('isPhoneVisible is false only for hidden areas', () => {
    for (const area of navigationAreas) {
      expect(isPhoneVisible(area)).toBe(area.phone !== 'hidden');
    }
    expect(navigationAreas.filter((a) => !isPhoneVisible(a)).map((a) => a.id)).toEqual(['execution']);
  });
});

describe('navigation groups (feature 205)', () => {
  it.each<[NavigationAreaId, string]>([
    ['queues', 'queues'],
    ['monitoring', 'monitoring'],
    ['notifications', 'notifications'],
    ['authoring', 'authoring'],
    ['execution-logs', 'settings'],
    ['execution', 'settings'],
    ['configuration', 'settings'],
    ['gamepacks', 'settings'],
    ['update', 'settings'],
    ['more', 'settings']
  ])('mainTabOf(%s) is %s', (area, tab) => {
    expect(mainTabOf(area)).toBe(tab);
  });

  it.each<[NavigationAreaId, string]>([
    ['queues', 'queues'],
    ['monitoring', 'monitoring'],
    ['notifications', 'notifications'],
    ['more', 'more'],
    ['configuration', 'more'],
    ['authoring', 'more'],
    ['update', 'more']
  ])('phoneTabOf(%s) is %s', (area, tab) => {
    expect(phoneTabOf(area)).toBe(tab);
  });

  it('hasMoreBack is true only for the pages and notices that the More page lists', () => {
    const withBack = navigationAreas.filter((a) => hasMoreBack(a.id)).map((a) => a.id);
    expect(withBack).toEqual(['authoring', 'execution-logs', 'configuration', 'gamepacks', 'update']);
  });

  it('desktopArea maps more to execution-logs and keeps any other area', () => {
    expect(desktopArea('more')).toBe('execution-logs');
    expect(desktopArea('queues')).toBe('queues');
    expect(desktopArea('update')).toBe('update');
  });

  it('lists the Settings sub-tabs in order', () => {
    expect(SETTINGS_SUB_TABS).toEqual(['execution-logs', 'execution', 'configuration', 'gamepacks', 'update']);
    expect(SETTINGS_FIRST).toBe('execution-logs');
  });

  it('lists the More rows in order', () => {
    expect(MORE_ROWS).toEqual(['configuration', 'gamepacks', 'update']);
    expect(MORE_DESKTOP_ONLY_ROWS).toEqual(['execution-logs', 'authoring']);
  });

  it('lists the Authoring tabs in order', () => {
    expect(AUTHORING_TABS).toEqual(['Commands', 'Games', 'Sequences', 'Images', 'Backup & Restore']);
  });

  it('lists the 5 desktop top tabs and the 4 phone tabs', () => {
    expect(MAIN_TABS.map((t) => t.label)).toEqual(['Queues', 'Monitor', 'Alerts', 'Authoring', 'Settings']);
    expect(PHONE_TABS.map((t) => t.label)).toEqual(['Queues', 'Monitor', 'Alerts', 'More']);
    expect(PHONE_TABS.map((t) => t.icon)).toEqual(['queue', 'monitor', 'alerts', 'more']);
  });
});
