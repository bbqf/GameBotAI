import React from 'react';
import './IconButton.css';

import { ICON_PATHS, ICON_NAMES, type IconName } from '../ui/icons';

export { ICON_NAMES };
export type { IconName };

export type IconButtonProps = {
  icon: IconName;
  /** The accessible name, for example "Edit Daily". Screen readers read it, and a test finds the button with it. */
  label: string;
  /** The tooltip, for example "Edit". The label is the tooltip when this is left out. */
  title?: string;
  /** The destructive tone is for an action that deletes. */
  tone?: 'default' | 'destructive';
  /** The touch size is 44 x 44 px, for a button on the phone (feature 184). The default size is for the big screen. */
  size?: 'default' | 'touch';
  disabled?: boolean;
  /** For a button that opens or closes a panel (feature 184). The button sets `aria-expanded` with it. */
  ariaExpanded?: boolean;
  onClick?: () => void;
};

/**
 * A small button that shows an icon (feature 173). It is a native button, so the keyboard works as for any button.
 * The icon has no text, so `label` is required.
 */
export const IconButton: React.FC<IconButtonProps> = ({
  icon,
  label,
  title,
  tone = 'default',
  size = 'default',
  disabled,
  ariaExpanded,
  onClick,
}) => (
  <button
    type="button"
    className={['icon-button', tone === 'destructive' ? 'icon-button--destructive' : '', size === 'touch' ? 'icon-button--touch' : '']
      .filter(Boolean)
      .join(' ')}
    aria-label={label}
    aria-expanded={ariaExpanded}
    title={title ?? label}
    disabled={disabled}
    onClick={onClick}
  >
    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">
      <path d={ICON_PATHS[icon]} fill="currentColor" />
    </svg>
  </button>
);
