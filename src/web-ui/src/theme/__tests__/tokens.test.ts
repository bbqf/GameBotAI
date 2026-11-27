import fs from 'fs';
import path from 'path';
import { cssVarMap } from '../tokens';

const css = fs.readFileSync(path.join(__dirname, '..', 'global.css'), 'utf8');
const normalize = (value: string) => value.replace(/\s+/g, ' ').trim();

describe('theme tokens and global.css', () => {
  it('declares each token variable in :root with the same value', () => {
    for (const [name, value] of Object.entries(cssVarMap)) {
      const match = new RegExp(`${name}:\\s*([^;]+);`).exec(css);
      expect(`${name} ${match ? normalize(match[1]) : 'missing'}`).toBe(`${name} ${normalize(value)}`);
    }
  });

  it('does not load a web font from the network', () => {
    expect(css).not.toMatch(/@import[^;]*fonts\.googleapis\.com/);
  });
});