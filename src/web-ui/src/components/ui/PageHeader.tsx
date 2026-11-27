import React from 'react';
import { IconButton } from './IconButton';
import { StatusChip, type ChipState } from './StatusChip';

export type PageHeaderProps = {
  title: string;
  /** Shows a back button with the label "Back". */
  onBack?: () => void;
  status?: { state: ChipState; text?: string };
  /** The actions on the right of the header. */
  actions?: React.ReactNode;
};

/** The header of a page: an optional back button, the title, an optional status chip, and the actions. */
export const PageHeader: React.FC<PageHeaderProps> = ({ title, onBack, status, actions }) => (
  <header className="ui-page-header">
    {onBack && <IconButton icon="back" label="Back" onClick={onBack} />}
    <h2 className="ui-page-header__title">{title}</h2>
    {status && <StatusChip state={status.state} text={status.text} />}
    {actions && <div className="ui-page-header__actions">{actions}</div>}
  </header>
);