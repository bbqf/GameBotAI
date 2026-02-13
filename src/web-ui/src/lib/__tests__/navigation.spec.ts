import { buildUnifiedUrl, navigateToUnified, normalizeTab } from '../navigation';

describe('navigation helpers', () => {
  const originalLocation = window.location;

  const setLocationAssign = (assign: jest.Mock) => {
    // Replace location to allow spying on assign in jsdom.
    delete (window as any).location;
    Object.defineProperty(window, 'location', {
      writable: true,
      value: { ...originalLocation, assign }
    });
  };

  const restoreLocation = () => {
    delete (window as any).location;
    Object.defineProperty(window, 'location', {
      writable: true,
      value: originalLocation
    });
  };

  afterEach(() => {
    restoreLocation();
    jest.restoreAllMocks();
  });

  it('normalizes tab values', () => {
    expect(normalizeTab(null)).toBeUndefined();
    expect(normalizeTab('ACTIONS')).toBe('Actions');
    expect(normalizeTab('unknown')).toBeUndefined();
  });

  it('builds unified urls with options', () => {
    expect(buildUnifiedUrl('Commands', { create: true })).toBe('/?tab=Commands&create=commands');
    expect(buildUnifiedUrl('Games', { id: 'game-1' })).toBe('/?tab=Games&id=game-1');
  });

  it('navigates in a new tab when requested', () => {
    const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

    navigateToUnified('Sequences', { newTab: true, id: 'seq-1' });

    expect(openSpy).toHaveBeenCalledWith('/?tab=Sequences&id=seq-1', '_blank');
  });

  it('navigates in the current tab by default', () => {
    const assignSpy = jest.fn();
    setLocationAssign(assignSpy);

    navigateToUnified('Images', { create: true });

    expect(assignSpy).toHaveBeenCalledWith('/?tab=Images&create=images');
  });
});
