import type { ParameterDeclaration } from '../types';

/** The props that every value field of a parameter takes (feature 168). */
export type ParameterFieldProps = {
  declaration: ParameterDeclaration;
  /** The technical value that is stored now. Null or empty means no value. */
  value: string | null | undefined;
  /** Called with the new technical value. It is not called with a new value that is invalid. */
  onChange: (value: string) => void;
  disabled?: boolean;
  /** Accessible name of the control. */
  ariaLabel: string;
  /** The selected items of a multi-value parameter, in order (feature 179). Used only when `declaration.multiple` is set. */
  values?: readonly string[];
  /** Called with the new ordered list of a multi-value parameter (feature 179). */
  onChangeValues?: (values: string[]) => void;
};

/** The text that shows the limits of a list, or null when the declaration has no limit (feature 179). */
export const limitsText = (declaration: Pick<ParameterDeclaration, 'minItems' | 'maxItems'>): string | null => {
  const { minItems, maxItems } = declaration;
  if (minItems != null && maxItems != null) return minItems === maxItems ? `Select ${minItems}.` : `Select ${minItems} to ${maxItems}.`;
  if (minItems != null) return `Select at least ${minItems}.`;
  if (maxItems != null) return `Select at most ${maxItems}.`;
  return null;
};

/** Adds the item at the end of the list, or removes it when it is in the list. The order of the others stays. */
export const toggleListItem = (items: readonly string[], item: string): string[] =>
  items.includes(item) ? items.filter((i) => i !== item) : [...items, item];

/** More than this many items show a search box (feature 168). */
export const SEARCH_THRESHOLD = 12;

/** A choice with this many options or fewer shows buttons. A longer one shows a list. */
export const BUTTON_LIMIT = 6;
