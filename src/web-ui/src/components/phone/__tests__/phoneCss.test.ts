import { readFileSync } from 'fs';
import { join } from 'path';

type Block = { prelude: string; body: string; start: number };

/** Returns top-level blocks of a CSS text (prelude + brace-matched body). */
const topLevelBlocks = (css: string): Block[] => {
  const blocks: Block[] = [];
  let depth = 0;
  let preludeStart = 0;
  let bodyStart = 0;
  let prelude = '';
  for (let i = 0; i < css.length; i++) {
    const ch = css[i];
    if (ch === '{') {
      if (depth === 0) {
        prelude = css.slice(preludeStart, i).trim();
        bodyStart = i + 1;
      }
      depth++;
    } else if (ch === '}') {
      depth--;
      if (depth === 0) {
        blocks.push({ prelude, body: css.slice(bodyStart, i), start: preludeStart });
        preludeStart = i + 1;
      }
    }
  }
  return blocks;
};

const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');

const cssFiles = [
  join(__dirname, '..', 'phone.css'),
  join(__dirname, '..', '..', '..', 'pages', 'Execution.phone.css')
];

describe.each(cssFiles)('phone css file %s', (file) => {
  const css = stripComments(readFileSync(file, 'utf8'));
  const blocks = topLevelBlocks(css);

  it('has no :hover rule outside @media (hover: hover)', () => {
    for (const block of blocks) {
      const isHoverMedia = /^@media\s*\(hover:\s*hover\)/.test(block.prelude);
      if (isHoverMedia) continue;
      expect(`${block.prelude}${block.body}`).not.toMatch(/:hover/);
    }
  });

  it('has only @media blocks at the top level', () => {
    for (const block of blocks) {
      expect(block.prelude).toMatch(/^@media/);
    }
  });

  it('scopes phone rules to the phone query', () => {
    const phoneBlocks = blocks.filter((b) => b.prelude.includes('max-width: 768px'));
    expect(phoneBlocks.length).toBeGreaterThan(0);
    for (const block of blocks) {
      const isHover = /\(hover:\s*hover\)/.test(block.prelude);
      const isPhone = block.prelude.includes('(max-width: 768px), (max-height: 500px)');
      expect(isHover || isPhone).toBe(true);
    }
  });
});
