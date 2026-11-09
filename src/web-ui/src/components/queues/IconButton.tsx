import React from 'react';
import './IconButton.css';

/** The icons of the Queues list and the template editor. Each is a 24 px drawing. No icon library is needed. */
const CLOSE_PATH = 'M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z';

const ICON_PATHS = {
  play: 'M8 5v14l11-7z',
  pause: 'M6 5h4v14H6zM14 5h4v14h-4z',
  stop: 'M6 6h12v12H6z',
  undo: 'M12 5V1L7 6l5 5V7c3.3 0 6 2.7 6 6s-2.7 6-6 6-6-2.7-6-6H4c0 4.4 3.6 8 8 8s8-3.6 8-8-3.6-8-8-8z',
  edit: 'M3 17.25V21h3.75L17.8 9.94l-3.75-3.75L3 17.25zM20.7 7.04a1 1 0 000-1.41l-2.34-2.34a1 1 0 00-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z',
  duplicate: 'M16 1H4a2 2 0 00-2 2v14h2V3h12V1zm3 4H8a2 2 0 00-2 2v14a2 2 0 002 2h11a2 2 0 002-2V7a2 2 0 00-2-2zm0 16H8V7h11v14z',
  delete: 'M6 19a2 2 0 002 2h8a2 2 0 002-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z',
  // Feature 184: Run next (a play arrow with a bar) and the icons of the template editor.
  runNext: 'M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z',
  parameters: 'M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z',
  remove: CLOSE_PATH,
  cancel: 'M12 2a10 10 0 100 20 10 10 0 000-20zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z',
  add: 'M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z',
  save: 'M17 3H5a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2V7l-4-4zm-5 16a3 3 0 110-6 3 3 0 010 6zm3-10H5V5h10v4z',
  reload: 'M17.65 6.35A7.96 7.96 0 0012 4a8 8 0 108 8h-2a6 6 0 11-1.76-4.24L13 11h7V4l-2.35 2.35z',
  link: 'M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7a5 5 0 000 10h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4a5 5 0 000-10z',
  unlink: 'M17 7h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1 0 1.43-.98 2.63-2.31 3l1.46 1.44C20.88 15.61 22 13.95 22 12c0-2.76-2.24-5-5-5zm-1 4h-2.19l2 2H16v-2zM2 4.27l3.11 3.11C3.29 8.12 2 9.91 2 12c0 2.76 2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1 0-1.59 1.21-2.9 2.76-3.07L8.73 11H8v2h2.73L13 15.27V17h1.73l4.01 4L20 19.74 3.27 3 2 4.27z',
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
