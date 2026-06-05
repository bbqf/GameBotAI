import { calcGestureDisplacement } from '../usePickerState';
import type { ImageMatchResult } from '../../../../types/picker';

// ── calcGestureDisplacement ────────────────────────────────────────────────────

describe('calcGestureDisplacement', () => {
  it('returns 0 for identical points', () => {
    expect(calcGestureDisplacement({ x: 10, y: 10 }, { x: 10, y: 10 })).toBe(0);
  });

  it('returns correct Euclidean distance for a 3-4-5 triangle', () => {
    expect(calcGestureDisplacement({ x: 0, y: 0 }, { x: 3, y: 4 })).toBeCloseTo(5, 5);
  });

  it('returns a value >= 10 when displacement is at least 10px', () => {
    expect(calcGestureDisplacement({ x: 0, y: 0 }, { x: 10, y: 0 })).toBeGreaterThanOrEqual(10);
    expect(calcGestureDisplacement({ x: 0, y: 0 }, { x: 8, y: 6 })).toBeGreaterThanOrEqual(10);
  });

  it('returns a value < 10 when displacement is less than 10px', () => {
    expect(calcGestureDisplacement({ x: 0, y: 0 }, { x: 3, y: 4 })).toBeLessThan(10);
    expect(calcGestureDisplacement({ x: 100, y: 100 }, { x: 101, y: 100 })).toBeLessThan(10);
  });

  it('is symmetric', () => {
    const a = { x: 5, y: 10 };
    const b = { x: 20, y: 30 };
    expect(calcGestureDisplacement(a, b)).toBeCloseTo(calcGestureDisplacement(b, a), 5);
  });
});

// ── recordTap offset logic (pure function extracted for testing) ───────────────

type BBox = { x: number; y: number; width: number; height: number; confidence: number };

function computeTapOffset(naturalX: number, naturalY: number, matches: (ImageMatchResult & BBox)[]) {
  const hits = matches.filter(
    (m) => naturalX >= m.x && naturalX <= m.x + m.width && naturalY >= m.y && naturalY <= m.y + m.height
  );
  if (hits.length === 0) return null;
  const best = hits.reduce((a, b) => (b.confidence > a.confidence ? b : a));
  const centerX = best.x + best.width / 2;
  const centerY = best.y + best.height / 2;
  return {
    imageId: best.imageId,
    offsetX: Math.round(naturalX - centerX),
    offsetY: Math.round(naturalY - centerY),
  };
}

describe('recordTap offset calculation', () => {
  const match: ImageMatchResult = {
    imageId: 'btn',
    imageName: 'btn',
    x: 100,
    y: 200,
    width: 80,
    height: 40,
    confidence: 0.9,
  };

  it('returns offsetX=0, offsetY=0 for a center click', () => {
    const result = computeTapOffset(140, 220, [match]);
    expect(result).not.toBeNull();
    expect(result!.offsetX).toBe(0);
    expect(result!.offsetY).toBe(0);
  });

  it('returns correct signed offset for an off-center click', () => {
    const result = computeTapOffset(110, 210, [match]);
    expect(result).not.toBeNull();
    expect(result!.offsetX).toBe(-30); // 110 - 140 = -30
    expect(result!.offsetY).toBe(-10); // 210 - 220 = -10
  });

  it('returns null when click is outside all bboxes', () => {
    const result = computeTapOffset(0, 0, [match]);
    expect(result).toBeNull();
  });

  it('picks the highest-confidence match when bboxes overlap', () => {
    const low: ImageMatchResult = { ...match, imageId: 'low', confidence: 0.5 };
    const high: ImageMatchResult = { ...match, imageId: 'high', confidence: 0.95 };
    const result = computeTapOffset(140, 220, [low, high]);
    expect(result!.imageId).toBe('high');
  });
});
