import React from 'react';

export type PhoneCardAction = {
  label: string;
  onClick: () => void;
  disabled?: boolean;
};

export type PhoneCardProps = {
  title: string;
  /** Optional status chip. */
  status?: React.ReactNode;
  /** At most one primary action. */
  action?: PhoneCardAction;
  /** Called when the user taps the card body. */
  onOpen?: () => void;
  testId?: string;
};

/** Tap-to-open card for a list row on a phone (contract C-07). */
export const PhoneCard: React.FC<PhoneCardProps> = ({ title, status, action, onOpen, testId }) => (
  <li className="phone-card" data-testid={testId}>
    <button type="button" className="phone-card__body" onClick={onOpen} aria-label={`Open ${title}`}>
      <span className="phone-card__title" title={title}>{title}</span>
      {status}
    </button>
    {action && (
      <button type="button" className="phone-card__action" onClick={action.onClick} disabled={action.disabled}>
        {action.label}
      </button>
    )}
  </li>
);
