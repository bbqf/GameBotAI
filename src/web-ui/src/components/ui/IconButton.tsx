import React from 'react';
import { Icon } from './Icon';
import type { IconName } from './icons';

export type IconButtonProps = {
  icon: IconName;
  /** The accessible name, for example "Edit Daily". */
  label: string;
  /** The tooltip. The label is the tooltip when this is left out. */
  title?: string;
  tone?: 'default' | 'danger';
  /** The touch size is 44 x 44 px. */
  size?: 'default' | 'touch';
  disabled?: boolean;
  ariaExpanded?: boolean;
  ariaHasPopup?: 'menu' | 'dialog' | boolean;
  onClick?: (event: React.MouseEvent<HTMLButtonElement>) => void;
  className?: string;
  testId?: string;
};

/** A button that shows only an icon. It is a native button, so the keyboard works as for any button. */
export const IconButton = React.forwardRef<HTMLButtonElement, IconButtonProps>(
  ({ icon, label, title, tone = 'default', size = 'default', disabled, ariaExpanded, ariaHasPopup, onClick, className, testId }, ref) => (
    <button
      ref={ref}
      type="button"
      className={['ui-icon-button', tone === 'danger' ? 'ui-icon-button--danger' : '', size === 'touch' ? 'ui-icon-button--touch' : '', className ?? '']
        .filter(Boolean)
        .join(' ')}
      aria-label={label}
      aria-expanded={ariaExpanded}
      aria-haspopup={ariaHasPopup}
      title={title ?? label}
      disabled={disabled}
      data-testid={testId}
      onClick={onClick}
    >
      <Icon name={icon} />
    </button>
  )
);
IconButton.displayName = 'IconButton';