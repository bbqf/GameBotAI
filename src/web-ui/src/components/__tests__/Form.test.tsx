import { validateRequired, tryParseJson } from '../Form';

describe('Form helpers', () => {
  it('validateRequired returns error when empty', () => {
    expect(validateRequired('', 'Name')).toBe('Name is required');
    expect(validateRequired('   ', 'Name')).toBe('Name is required');
  });

  it('validateRequired returns null when value present', () => {
    expect(validateRequired('Hello', 'Name')).toBeNull();
  });

  it('tryParseJson parses valid JSON', () => {
    const res = tryParseJson('{"a":1}');
    expect(res.error).toBeUndefined();
    expect(res.value).toEqual({ a: 1 });
  });

  it('tryParseJson returns undefined value for empty', () => {
    const res = tryParseJson('   ');
    expect(res.value).toBeUndefined();
    expect(res.error).toBeUndefined();
  });

  it('tryParseJson returns error for invalid JSON', () => {
    const res = tryParseJson('{');
    expect(res.error).toBe('Invalid JSON');
  });
});
