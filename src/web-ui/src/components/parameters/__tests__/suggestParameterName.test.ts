import { suggestParameterName } from '../suggestParameterName';
import { validateParameterName } from '../parameterNameRules';

describe('suggestParameterName', () => {
  it('makes a camelCase identifier from the label', () => {
    expect(suggestParameterName('Emulator instance', [])).toBe('emulatorInstance');
    expect(suggestParameterName('  Speed (ms)  ', [])).toBe('speedMs');
  });

  it('makes a valid identifier when the label starts with a digit', () => {
    const name = suggestParameterName('3rd attempt', []);
    expect(validateParameterName(name, [])).toBeUndefined();
  });

  it('is unique among the other names, without case', () => {
    expect(suggestParameterName('Speed', ['speed'])).toBe('speed2');
    expect(suggestParameterName('Speed', ['speed', 'speed2'])).toBe('speed3');
  });

  it('avoids reserved names', () => {
    expect(validateParameterName(suggestParameterName('Iteration', []), [])).toBeUndefined();
    expect(validateParameterName(suggestParameterName('Queue', []), [])).toBeUndefined();
  });

  it('returns an empty text when the label has no letter or digit', () => {
    expect(suggestParameterName('!!!', [])).toBe('');
    expect(suggestParameterName('', [])).toBe('');
  });
});
