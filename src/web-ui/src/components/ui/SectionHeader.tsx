import React from 'react';

export type SectionHeaderProps = {
  title: string;
  /** The actions on the right of the title. */
  actions?: React.ReactNode;
  id?: string;
};

/** The header of a section of a page: a title and optional actions. */
export const SectionHeader: React.FC<SectionHeaderProps> = ({ title, actions, id }) => (
  <div className="ui-section-header">
    <h3 id={id}>{title}</h3>
    {actions && <div className="ui-section-header__actions">{actions}</div>}
  </div>
);
