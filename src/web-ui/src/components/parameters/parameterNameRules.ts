import { BUILT_IN_PREFIX } from './types';

const RESERVED_ITERATION = 'iteration';
const IDENTIFIER = /^[A-Za-z_]\w*$/;

/**
 * Mirrors the backend's ParameterNameRules so the operator gets the message while typing rather than
 * on save. The backend remains authoritative — this is a convenience, not the enforcement point.
 */
export const validateParameterName = (
  name: string,
  others: readonly string[],
): string | undefined => {
  if (!name.trim()) return 'Name is required.';
  if (name === RESERVED_ITERATION) return "'iteration' is reserved for the loop iteration value.";
  if (name === 'queue' || name.startsWith(BUILT_IN_PREFIX)) {
    return "Names may not use the reserved 'queue' namespace.";
  }
  if (!IDENTIFIER.test(name)) {
    return 'Use letters, digits and underscore; do not start with a digit.';
  }
  if (others.some((other) => other.toLowerCase() === name.toLowerCase())) {
    return 'Another parameter already uses this name.';
  }
  return undefined;
};
