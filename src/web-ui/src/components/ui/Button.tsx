import React from 'react';
import { Icon } from './Icon';
import type { IconName } from './icons';

export type ButtonVariant = 'primary' | 'secondary' | 'danger-outline' | 'danger';

export type ButtonProps = {
  variant?: ButtonVariant;
  icon?: IconName;
  type?: 'button' | 'submit' | 'reset';
  disabled?: boolean;
  onClick?: (event: React.MouseEvent<HTMLButtonElement>) => void;
  className?: string;
  /** The accessible name, when the visible text is not enough (for example an icon glyph). */
  ariaLabel?: string;
  testId?: string;
  children?: React.ReactNode;
};

/** The shared text button. A page uses it instead of its own button style. */
export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ variant = 'secondary', icon, type = 'button', disabled, onClick, className, testId, ariaLabel, children }, ref) => (
    <button
      ref={ref}
      type={type}
      className={['ui-button', `ui-button--${variant}`, className ?? ''].filter(Boolean).join(' ')}
      disabled={disabled}
      data-testid={testId}
      aria-label={ariaLabel}
      onClick={onClick}
    >
      {icon ? <Icon name={icon} size={18} /> : null}
      {children}
    </button>
  )
);
Button.displayName = 'Button';