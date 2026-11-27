import fs from 'fs';
import path from 'path';

const css = fs.readFileSync(path.join(__dirname, '..', 'ui.css'), 'utf8');

describe('ui.css', () => {
  it('has no hex color: it uses only theme variables', () => {
    expect(css).not.toMatch(/#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?\b/);
  });

  it('has no :hover rule outside @media (hover: hover)', () => {
    const outside: string[] = [];
    let depth = 0;
    let hoverDepth = -1;
    for (const line of css.split('\n')) {
      if (/@media\s*\(hover:\s*hover\)/.test(line)) hoverDepth = depth;
      if (line.includes(':hover') && hoverDepth === -1) outside.push(line.trim());
      depth += (line.match(/{/g) ?? []).length - (line.match(/}/g) ?? []).length;
      if (hoverDepth !== -1 && depth <= hoverDepth) hoverDepth = -1;
    }
    expect(outside).toEqual([]);
  });
});