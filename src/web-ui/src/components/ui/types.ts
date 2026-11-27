import type { ReactNode } from 'react';
import type { IconName } from './icons';

/** One action of a list row. It shows as an icon button or as an item of the "more" menu. */
export type RowAction = {
  /** Unique in the row. */
  id: string;
  /** The action name, for example "Pause". It is the tooltip and the text of a menu item. */
  label: string;
  /** The accessible name, for example "Pause PNS Daily". The label is used when this is left out. */
  ariaLabel?: string;
  icon: IconName;
  tone?: 'default' | 'danger';
  /** An action that is off keeps its place and is dimmed. */
  disabled?: boolean;
  onSelect: () => void;
};

export type RowActions = {
  /** Desktop: at most 3. Phone: at most 1. Use the same count for all rows of a list. */
  visible: RowAction[];
  /** The items of the "more" menu. */
  menu?: RowAction[];
};

export type ListColumn<T> = {
  /** Unique in the list. */
  id: string;
  header: string;
  /** A width in px, or 'flex' for the one column that takes the free space. */
  width: number | 'flex';
  align?: 'start' | 'end';
  /** The content of the cell. Keep it to one line. */
  render: (row: T) => ReactNode;
  /** The tooltip text. When it is an empty string, the cell shows a dash. */
  text?: (row: T) => string;
  /** A `data-testid` for each cell of the column. */
  cellTestId?: string;
};

export type ListState =
  | 'ready'
  | { kind: 'loading' }
  | { kind: 'empty'; text?: string }
  | { kind: 'error'; text: string; onRetry?: () => void };