import { isPhoneVisible, navigationAreas } from '../navigation';

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

  it('adds Monitoring as a full phone area right after Queues (feature 141)', () => {
    expect(level('monitoring')).toBe('full');
    const monitoring = navigationAreas.find((a) => a.id === 'monitoring');
    expect(monitoring).toMatchObject({ label: 'Monitoring', path: '/monitoring', order: 1 });
  });

  it('has 9 areas with unique orders from 0 to 8, in the agreed sequence', () => {
    expect(navigationAreas).toHaveLength(9);
    const sorted = [...navigationAreas].sort((a, b) => a.order - b.order);
    expect(sorted.map((a) => a.order)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]);
    expect(sorted.map((a) => a.id)).toEqual([
      'queues', 'monitoring', 'authoring', 'execution', 'execution-logs', 'configuration', 'update', 'gamepacks', 'notifications'
    ]);
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
