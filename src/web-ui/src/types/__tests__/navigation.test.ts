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

  it('keeps the other levels', () => {
    expect(level('queues')).toBe('reduced');
    expect(level('notifications')).toBe('full');
    expect(level('configuration')).toBe('full');
    expect(level('update')).toBe('full');
  });

  it('isPhoneVisible is false only for hidden areas', () => {
    for (const area of navigationAreas) {
      expect(isPhoneVisible(area)).toBe(area.phone !== 'hidden');
    }
    expect(navigationAreas.filter((a) => !isPhoneVisible(a)).map((a) => a.id)).toEqual(['execution']);
  });
});
