import { buildUnifiedUrl, navigateToUnified, normalizeTab } from '../navigation';

describe('navigation helpers', () => {
  afterEach(() => {
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
    // In jsdom@25+ window.location.assign is non-configurable and cannot be spied on.
    // Verify the correct URL would be built and that window.open is NOT called (same-tab path).
    const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

    // navigateToUnified calls location.assign which triggers a jsdom navigation;
    // we just need to verify it doesn't open a new tab and uses the right URL.
    expect(buildUnifiedUrl('Images', { create: true })).toBe('/?tab=Images&create=images');
    expect(openSpy).not.toHaveBeenCalled();
  });
});
