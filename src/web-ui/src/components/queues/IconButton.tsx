import React from 'react';
import './IconButton.css';

/** The icons of the Queues list. Each is a 24 px drawing. No icon library is needed. */
const ICON_PATHS = {
  play: 'M8 5v14l11-7z',
  pause: 'M6 5h4v14H6zM14 5h4v14h-4z',
  stop: 'M6 6h12v12H6z',
  undo: 'M12 5V1L7 6l5 5V7c3.3 0 6 2.7 6 6s-2.7 6-6 6-6-2.7-6-6H4c0 4.4 3.6 8 8 8s8-3.6 8-8-3.6-8-8-8z',
  edit: 'M3 17.25V21h3.75L17.8 9.94l-3.75-3.75L3 17.25zM20.7 7.04a1 1 0 000-1.41l-2.34-2.34a1 1 0 00-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z',
  duplicate: 'M16 1H4a2 2 0 00-2 2v14h2V3h12V1zm3 4H8a2 2 0 00-2 2v14a2 2 0 002 2h11a2 2 0 002-2V7a2 2 0 00-2-2zm0 16H8V7h11v14z',
  delete: 'M6 19a2 2 0 002 2h8a2 2 0 002-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z',
} as const;

export type IconName = keyof typeof ICON_PATHS;

export const ICON_NAMES = Object.keys(ICON_PATHS) as IconName[];

export type IconButtonProps = {
  icon: IconName;
  /** The accessible name, for example "Edit Daily". Screen readers read it, and a test finds the button with it. */
  label: string;
  /** The tooltip, for example "Edit". The label is the tooltip when this is left out. */
  title?: string;
  /** The destructive tone is for an action that deletes. */
  tone?: 'default' | 'destructive';
  disabled?: boolean;
  onClick?: () => void;
};

/**
 * A small button that shows an icon (feature 173). It is a native button, so the keyboard works as for any button.
 * The icon has no text, so `label` is required.
 */
export const IconButton: React.FC<IconButtonProps> = ({ icon, label, title, tone = 'default', disabled, onClick }) => (
  <button
    type="button"
    className={tone === 'destructive' ? 'icon-button icon-button--destructive' : 'icon-button'}
    aria-label={label}
    title={title ?? label}
    disabled={disabled}
    onClick={onClick}
  >
    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">
      <path d={ICON_PATHS[icon]} fill="currentColor" />
    </svg>
  </button>
);
