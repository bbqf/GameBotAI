import { ICON_NAMES, ICON_PATHS } from '../icons';

const NEW_NAMES = ['play', 'pause', 'stop', 'edit', 'duplicate', 'delete', 'more', 'back', 'close', 'drag', 'expand', 'collapse', 'upload', 'download', 'search', 'filter', 'add', 'send'];
const OLD_NAMES = ['undo', 'runNext', 'parameters', 'settings', 'remove', 'cancel', 'save', 'reload', 'link', 'unlink'];

describe('icon set', () => {
  it.each([...NEW_NAMES, ...OLD_NAMES])('has the icon %s', (name) => {
    expect(ICON_NAMES).toContain(name);
  });

  it('has a non-empty path for each icon', () => {
    for (const name of ICON_NAMES) {
      expect(typeof ICON_PATHS[name]).toBe('string');
      expect(ICON_PATHS[name].length).toBeGreaterThan(0);
    }
  });
});