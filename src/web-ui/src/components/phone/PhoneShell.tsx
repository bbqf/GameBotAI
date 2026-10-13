import React from 'react';
import { NavigationArea, NavigationAreaId } from '../../types/navigation';
import { PhoneTabBar } from './PhoneTabBar';

export type PhoneHeaderProps = {
  title: string;
  /** When set, the header shows a back control. */
  onBack?: () => void;
};

/** Compact header with the area title and an optional back control (contract C-02). */
export const PhoneHeader: React.FC<PhoneHeaderProps> = ({ title, onBack }) => (
  <header className="phone-header">
    {onBack && (
      <button type="button" className="phone-header__back" onClick={onBack} aria-label="Back">←</button>
    )}
    <h1 className="phone-header__title">{title}</h1>
  </header>
);

export type PhoneShellProps = {
  areas: NavigationArea[];
  activeArea: NavigationAreaId;
  onChange: (id: NavigationAreaId) => void;
  onBack?: () => void;
  children: React.ReactNode;
};

/**
 * Phone shell: header, content region, bottom tab bar.
 * `App` renders the header and the tab bar around its own `<main>` so that area content stays mounted
 * when the layout mode changes. This composite serves isolated use and tests.
 */
export const PhoneShell: React.FC<PhoneShellProps> = ({ areas, activeArea, onChange, onBack, children }) => {
  const title = areas.find((a) => a.id === activeArea)?.label ?? '';
  return (
    <>
      <PhoneHeader title={title} onBack={onBack} />
      <main className="content">{children}</main>
      <PhoneTabBar areas={areas} activeArea={activeArea} onChange={onChange} />
    </>
  );
};
