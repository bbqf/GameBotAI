import type { ParameterDeclaration } from './types';
import { effectiveKind } from './types';

/**
 * Checks one value against its declaration (feature 168). The checks and the message texts match
 * `ParameterValueRules` in the domain. The service stays the authority; this check is for speed only,
 * so the form can say "wrong" before the save.
 *
 * Returns a message with a hint, or null when the value fits. A null, empty-inherit, or placeholder
 * value is not checked.
 */
export const checkParameterValue = (
  declaration: ParameterDeclaration,
  value: string | null | undefined,
): string | null => {
  if (value == null) return null;
  if (isWholePlaceholder(value)) return null;

  const { name } = declaration;
  switch (effectiveKind(declaration)) {
    case 'number':
      return checkNumber(declaration, value);
    case 'choice':
      if (declaration.options && declaration.options.length > 0
        && !declaration.options.some((o) => o.value === value)) {
        return `value '${value}' of parameter '${name}' is not one of its options. Select one of the options.`;
      }
      return null;
    case 'image':
      if (declaration.allowedImages && declaration.allowedImages.length > 0
        && !declaration.allowedImages.includes(value)) {
        return `image '${value}' of parameter '${name}' is not in the list of allowed images. Select one of the allowed images.`;
      }
      return null;
    default:
      return null;
  }
};

/** True when the text is exactly one `{{name}}` placeholder, with no other text. */
export const isWholePlaceholder = (value: string): boolean =>
  /^\{\{[^{}]+\}\}$/.test(value);

const checkNumber = (declaration: ParameterDeclaration, value: string): string | null => {
  const { name, min, max } = declaration;
  if (!/^-?\d+$/.test(value.trim())) {
    return `value '${value}' of parameter '${name}' is not a whole number. Enter a whole number.`;
  }
  const number = Number.parseInt(value, 10);
  if (min != null && number < min) {
    return `value ${number} of parameter '${name}' is below the minimum ${min}. Enter a number of ${min} or more.`;
  }
  if (max != null && number > max) {
    return `value ${number} of parameter '${name}' is above the maximum ${max}. Enter a number of ${max} or less.`;
  }
  return null;
};
