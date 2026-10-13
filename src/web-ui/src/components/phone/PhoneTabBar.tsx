import React, { useEffect, useState } from 'react';
import { NavigationArea, NavigationAreaId, isPhoneVisible } from '../../types/navigation';

export type PhoneTabBarProps = {
  areas: NavigationArea[];
  activeArea: NavigationAreaId;
  onChange: (id: NavigationAreaId) => void;
};

type TabDef = { id: NavigationAreaId; label: string; icon: string };

const MAIN_TABS: TabDef[] = [
  { id: 'queues', label: 'Queues', icon: '☰' },
  { id: 'monitoring', label: 'Monitoring', icon: '📺' }
];

const isTextInput = (el: Element | null): boolean => {
  if (!el) return false;
  const tag = el.tagName;
  if (tag === 'TEXTAREA' || tag === 'SELECT') return true;
  if (tag !== 'INPUT') return false;
  const type = (el as HTMLInputElement).type;
  return type !== 'checkbox' && type !== 'radio' && type !== 'button' && type !== 'submit';
};

/** Bottom tab bar and the "More" sheet (contract C-02). The bar hides while an input has focus. */
export const PhoneTabBar: React.FC<PhoneTabBarProps> = ({ areas, activeArea, onChange }) => {
  const [sheetOpen, setSheetOpen] = useState(false);
  const [inputFocused, setInputFocused] = useState(false);

  useEffect(() => {
    const update = () => setInputFocused(isTextInput(document.activeElement));
    document.addEventListener('focusin', update);
    document.addEventListener('focusout', update);
    return () => {
      document.removeEventListener('focusin', update);
      document.removeEventListener('focusout', update);
    };
  }, []);

  const mainIds = MAIN_TABS.map((t) => t.id);
  const moreActive = !mainIds.includes(activeArea);
  const moreAreas = [...areas]
    .filter((a) => isPhoneVisible(a) && !mainIds.includes(a.id))
    .sort((a, b) => a.order - b.order);

  const select = (id: NavigationAreaId) => {
    setSheetOpen(false);
    onChange(id);
  };

  return (
    <>
      {sheetOpen && (
        <div className="phone-sheet" role="dialog" aria-label="More areas">
          <ul className="phone-sheet__list">
            {moreAreas.map((area) => (
              <li key={area.id}>
                <button
                  type="button"
                  className="phone-sheet__item"
                  aria-current={area.id === activeArea ? 'page' : undefined}
                  onClick={() => select(area.id)}
                >
                  <span>{area.label}</span>
                  {area.phone === 'desktop-only' && <span className="phone-sheet__note">Desktop only</span>}
                </button>
              </li>
            ))}
          </ul>
          <button type="button" className="phone-sheet__item" onClick={() => setSheetOpen(false)}>Close</button>
        </div>
      )}
      <nav
        aria-label="Primary Navigation"
        className={inputFocused ? 'phone-tabbar phone-tabbar--hidden' : 'phone-tabbar'}
      >
        {MAIN_TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            className="phone-tabbar__item"
            aria-current={tab.id === activeArea ? 'page' : undefined}
            aria-label={tab.label}
            onClick={() => select(tab.id)}
          >
            <span className="phone-tabbar__icon" aria-hidden="true">{tab.icon}</span>
            <span>{tab.label}</span>
          </button>
        ))}
        <button
          type="button"
          className="phone-tabbar__item"
          aria-current={moreActive ? 'page' : undefined}
          aria-expanded={sheetOpen}
          onClick={() => setSheetOpen((open) => !open)}
        >
          <span className="phone-tabbar__icon" aria-hidden="true">⋯</span>
          <span>More</span>
        </button>
      </nav>
    </>
  );
};
