import { coerceValue, validateAttribute, validateAttributes } from '../validation';
import type { AttributeDefinition } from '../../types/actions';

describe('services validation helpers', () => {
  it('coerces values based on data type', () => {
    expect(coerceValue('string', undefined)).toEqual({ value: undefined });
    expect(coerceValue('boolean', 'yes')).toEqual({ value: true });
    expect(coerceValue('number', '3')).toEqual({ value: 3 });
    expect(coerceValue('number', 'bad')).toEqual({ error: 'Must be a number' });
    expect(coerceValue('enum', 3 as any)).toEqual({ error: 'Must be a string option' });
    expect(coerceValue('string', 5 as any)).toEqual({ value: '5' });
  });

  it('validates required fields', () => {
    const def: AttributeDefinition = { key: 'name', dataType: 'string', required: true };
    expect(validateAttribute(def, '')).toEqual({ field: 'name', message: 'This field is required' });
  });

  it('validates numeric constraints', () => {
    const def: AttributeDefinition = { key: 'count', dataType: 'number', required: true, constraints: { min: 2, max: 4 } };

    expect(validateAttribute(def, '3' as any)).toEqual({ field: 'count', message: 'Must be a number' });
    expect(validateAttribute(def, 1)).toEqual({ field: 'count', message: 'Must be at least 2' });
    expect(validateAttribute(def, 5)).toEqual({ field: 'count', message: 'Must be at most 4' });
    expect(validateAttribute(def, 3)).toBeUndefined();
  });

  it('validates boolean fields', () => {
    const def: AttributeDefinition = { key: 'enabled', dataType: 'boolean', required: false };

    expect(validateAttribute(def, 'true' as any)).toEqual({ field: 'enabled', message: 'Must be true or false' });
    expect(validateAttribute(def, true)).toBeUndefined();
  });

  it('validates enum and pattern constraints', () => {
    const enumDef: AttributeDefinition = {
      key: 'mode',
      dataType: 'enum',
      required: true,
      constraints: { allowedValues: ['fast', 'slow'] }
    };
    const patternDef: AttributeDefinition = {
      key: 'tag',
      dataType: 'string',
      required: true,
      constraints: { pattern: '^test-' }
    };

    expect(validateAttribute(enumDef, 1 as any)).toEqual({ field: 'mode', message: 'Must be a string option' });
    expect(validateAttribute(enumDef, 'medium')).toEqual({ field: 'mode', message: 'Value not allowed' });
    expect(validateAttribute(enumDef, 'fast')).toBeUndefined();

    expect(validateAttribute(patternDef, 3 as any)).toEqual({ field: 'tag', message: 'Must be text' });
    expect(validateAttribute(patternDef, 'bad')).toEqual({ field: 'tag', message: 'Does not match required pattern' });
    expect(validateAttribute(patternDef, 'test-ok')).toBeUndefined();
  });

  it('validates multiple attributes', () => {
    const defs: AttributeDefinition[] = [
      { key: 'a', dataType: 'string', required: true },
      { key: 'b', dataType: 'number', required: true }
    ];

    const result = validateAttributes(defs, { a: '', b: 'nope' });

    expect(result).toHaveLength(2);
    expect(result[0].field).toBe('a');
    expect(result[1].field).toBe('b');
  });
});
