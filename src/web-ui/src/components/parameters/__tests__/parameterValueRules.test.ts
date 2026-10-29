import { checkParameterValue } from '../parameterValueRules';
import type { ParameterDeclaration } from '../types';

const number = (min?: number, max?: number): ParameterDeclaration => ({
  name: 'n', type: 'number', required: false, min, max,
});

const choice: ParameterDeclaration = {
  name: 'c', type: 'text', required: false, kind: 'choice',
  options: [{ label: 'Fast', value: 'f' }, { label: 'Slow', value: 's' }],
};

const image: ParameterDeclaration = {
  name: 'i', type: 'text', required: false, kind: 'image', allowedImages: ['a', 'b'],
};

describe('checkParameterValue', () => {
  it('does not check a null value or a placeholder', () => {
    expect(checkParameterValue(number(1, 2), null)).toBeNull();
    expect(checkParameterValue(choice, '{{other}}')).toBeNull();
    expect(checkParameterValue(image, '{{other}}')).toBeNull();
  });

  it('accepts a number in range', () => {
    expect(checkParameterValue(number(1, 9), '5')).toBeNull();
    expect(checkParameterValue(number(1, 9), '1')).toBeNull();
    expect(checkParameterValue(number(1, 9), '9')).toBeNull();
    expect(checkParameterValue(number(), '-5')).toBeNull();
  });

  it.each(['abc', '1.5', '0', '10'])('refuses %s with a hint', (value) => {
    const message = checkParameterValue(number(1, 9), value);
    expect(message).not.toBeNull();
    expect(message).toContain('. ');
  });

  it('checks the option values of a choice', () => {
    expect(checkParameterValue(choice, 'f')).toBeNull();
    expect(checkParameterValue(choice, 'zzz')).toContain('Select one of the options');
  });

  it('checks the allowed images', () => {
    expect(checkParameterValue(image, 'a')).toBeNull();
    expect(checkParameterValue(image, 'zzz')).toContain('Select one of the allowed images');
  });

  it('accepts any text for a text parameter', () => {
    expect(checkParameterValue({ name: 't', type: 'text', required: false }, 'anything')).toBeNull();
  });
});
