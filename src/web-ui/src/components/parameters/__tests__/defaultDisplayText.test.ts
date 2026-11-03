import { defaultDisplayText } from '../types';

describe('defaultDisplayText', () => {
  it('returns the option label for a choice', () => {
    expect(
      defaultDisplayText({
        type: 'text',
        kind: 'choice',
        default: 'a',
        options: [{ label: 'Option A', value: 'a' }],
      }),
    ).toBe('Option A');
  });

  it('falls back to the raw value for an unknown choice value', () => {
    expect(
      defaultDisplayText({
        type: 'text',
        kind: 'choice',
        default: 'zzz',
        options: [{ label: 'Option A', value: 'a' }],
      }),
    ).toBe('zzz');
  });

  it('returns number text', () => {
    expect(defaultDisplayText({ type: 'number', default: '5000' })).toBe('5000');
  });

  it('returns the image id', () => {
    expect(defaultDisplayText({ type: 'text', kind: 'image', default: 'img-1' })).toBe('img-1');
  });

  it('returns (empty) for an empty string', () => {
    expect(defaultDisplayText({ type: 'text', default: '' })).toBe('(empty)');
  });

  it('returns No default for null and undefined', () => {
    expect(defaultDisplayText({ type: 'text', default: null })).toBe('No default');
    expect(defaultDisplayText({ type: 'text' })).toBe('No default');
  });
});
