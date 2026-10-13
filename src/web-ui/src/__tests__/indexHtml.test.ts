import { readFileSync } from 'fs';
import { join } from 'path';

const root = join(__dirname, '..', '..');
const html = readFileSync(join(root, 'index.html'), 'utf8');

describe('index.html install metadata (contract C-11)', () => {
  it('links the manifest', () => {
    expect(html).toMatch(/<link rel="manifest" href="\/manifest\.webmanifest"/);
  });

  it('sets theme-color and the Apple standalone tags and touch icon', () => {
    expect(html).toMatch(/<meta name="theme-color"/);
    expect(html).toMatch(/<meta name="apple-mobile-web-app-capable" content="yes"/);
    expect(html).toMatch(/<link rel="apple-touch-icon" href="\/icons\/apple-touch-icon\.png"/);
  });

  it('uses viewport-fit=cover', () => {
    expect(html).toMatch(/<meta name="viewport"[^>]*viewport-fit=cover/);
  });
});

describe('manifest.webmanifest (contract C-11)', () => {
  const manifest = JSON.parse(readFileSync(join(root, 'public', 'manifest.webmanifest'), 'utf8'));

  it('has the required fields', () => {
    for (const key of ['name', 'short_name', 'display', 'start_url', 'theme_color', 'background_color']) {
      expect(manifest[key]).toBeTruthy();
    }
    expect(manifest.display).toBe('standalone');
  });

  it('has 192 and 512 icons and one maskable icon, and the files exist', () => {
    const sizes = manifest.icons.map((i: { sizes: string }) => i.sizes);
    expect(sizes).toEqual(expect.arrayContaining(['192x192', '512x512']));
    expect(manifest.icons.some((i: { purpose?: string }) => i.purpose === 'maskable')).toBe(true);
    for (const icon of manifest.icons as Array<{ src: string }>) {
      expect(() => readFileSync(join(root, 'public', icon.src))).not.toThrow();
    }
    expect(() => readFileSync(join(root, 'public', 'icons', 'apple-touch-icon.png'))).not.toThrow();
  });
});
