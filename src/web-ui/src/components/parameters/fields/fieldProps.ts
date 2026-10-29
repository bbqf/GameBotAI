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
};

/** More than this many items show a search box (feature 168). */
export const SEARCH_THRESHOLD = 12;

/** A choice with this many options or fewer shows buttons. A longer one shows a list. */
export const BUTTON_LIMIT = 6;
