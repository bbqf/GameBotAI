import { navigationAreas } from '../navigation';

describe('navigationAreas phone support level', () => {
  it('gives each area a level', () => {
    for (const area of navigationAreas) {
      expect(['full', 'reduced', 'desktop-only']).toContain(area.phone);
    }
  });

  it('marks authoring and execution-logs as desktop-only', () => {
    const level = (id: string) => navigationAreas.find((a) => a.id === id)?.phone;
    expect(level('authoring')).toBe('desktop-only');
    expect(level('execution-logs')).toBe('desktop-only');
  });

  it('marks queues as reduced', () => {
    expect(navigationAreas.find((a) => a.id === 'queues')?.phone).toBe('reduced');
  });
});
