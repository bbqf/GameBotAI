import { validateParameterName } from './parameterNameRules';

/**
 * Makes a parameter name from the label the author wrote (feature 168): a camelCase identifier that is
 * valid and not used by another parameter. Returns an empty text when the label has no letter or digit.
 *
 * @param label The label of the parameter.
 * @param others The names of the other parameters of the same item.
 */
export const suggestParameterName = (label: string, others: readonly string[]): string => {
  const words = label
    .normalize('NFKD')
    .replace(/[^A-Za-z0-9]+/g, ' ')
    .trim()
    .split(/\s+/)
    .filter((word) => word.length > 0);
  if (words.length === 0) return '';

  let base = words
    .map((word, index) => {
      const lower = word.toLowerCase();
      return index === 0 ? lower : lower.charAt(0).toUpperCase() + lower.slice(1);
    })
    .join('');
  if (/^\d/.test(base)) base = `p${base.charAt(0).toUpperCase()}${base.slice(1)}`;

  let candidate = base;
  let counter = 2;
  while (validateParameterName(candidate, others) !== undefined) {
    candidate = `${base}${counter}`;
    counter += 1;
    if (counter > 99) return '';
  }
  return candidate;
};
