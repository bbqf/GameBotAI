import React from 'react';

export type CollapsibleSectionProps = {
  title: string;
  defaultOpen?: boolean;
  children: React.ReactNode;
};

export const CollapsibleSection: React.FC<CollapsibleSectionProps> = ({ title, defaultOpen, children }) => {
  return (
    <details className="collapsible-section" open={defaultOpen}>
      <summary className="collapsible-section-title">{title}</summary>
      <div className="collapsible-section-content">
        {children}
      </div>
    </details>
  );
};
