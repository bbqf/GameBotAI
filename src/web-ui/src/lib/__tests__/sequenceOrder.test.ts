import { sameSequenceOrder } from '../sequenceOrder';

describe('sameSequenceOrder', () => {
  it('returns true for equal arrays in the same order', () => {
    expect(sameSequenceOrder(['a', 'b', 'c'], ['a', 'b', 'c'])).toBe(true);
  });

  it('returns true for two empty arrays', () => {
    expect(sameSequenceOrder([], [])).toBe(true);
  });

  it('returns false when lengths differ', () => {
    expect(sameSequenceOrder(['a', 'b'], ['a', 'b', 'c'])).toBe(false);
    expect(sameSequenceOrder([], ['a'])).toBe(false);
  });

  it('returns false when the order differs', () => {
    expect(sameSequenceOrder(['a', 'b'], ['b', 'a'])).toBe(false);
  });

  it('respects duplicates and their positions', () => {
    expect(sameSequenceOrder(['a', 'a', 'b'], ['a', 'a', 'b'])).toBe(true);
    expect(sameSequenceOrder(['a', 'a', 'b'], ['a', 'b', 'a'])).toBe(false);
  });
});
