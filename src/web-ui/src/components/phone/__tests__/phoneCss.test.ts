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

describe('phone queue activity styles (FR-008, SC-006)', () => {
  const css = stripComments(readFileSync(cssFiles[0], 'utf8'));
  const rule = (selector: string): string => {
    const escaped = selector.replace(/[.[\]"=]/g, (ch) => `\\${ch}`);
    const match = new RegExp(`${escaped}\\s*\\{([^}]*)\\}`).exec(css);
    return match ? match[1] : '';
  };

  it('gives each new control a tap target of at least 44 px', () => {
    expect(rule('.phone-schedule-row')).toMatch(/min-height:\s*44px/);
    expect(rule('.phone-schedule-row input[type="checkbox"]')).toMatch(/min-height:\s*44px/);
    expect(rule('.phone-queue-detail button')).toMatch(/min-height:\s*44px/);
  });

  it('sets no sideways scroll on the activity view', () => {
    expect(rule('.phone-queue-activity')).not.toMatch(/overflow-x:\s*(scroll|auto)/);
    expect(rule('.phone-queue-activity')).toMatch(/overflow-wrap:\s*anywhere/);
    expect(rule('.phone-card__summary')).toMatch(/text-overflow:\s*ellipsis/);
  });

  it('uses no fixed width above 320 px', () => {
    const block = css.slice(css.indexOf('.phone-queue-activity {'), css.indexOf('.phone-stale {') + 200);
    const widths = [...block.matchAll(/(?:^|[\s;])(?:min-)?width:\s*(\d+)px/g)].map((m) => Number(m[1]));
    for (const w of widths) expect(w).toBeLessThanOrEqual(320);
  });
});
