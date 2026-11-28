import React, { useEffect, useState } from 'react';
import { Icon } from '../ui/Icon';
import { NavigationArea, NavigationAreaId, PHONE_TABS, phoneTabOf } from '../../types/navigation';

export type PhoneTabBarProps = {
  areas?: NavigationArea[];
  activeArea: NavigationAreaId;
  onChange: (id: NavigationAreaId) => void;
};

const isTextInput = (el: Element | null): boolean => {
  if (!el) return false;
  const tag = el.tagName;
  if (tag === 'TEXTAREA' || tag === 'SELECT') return true;
  if (tag !== 'INPUT') return false;
  const type = (el as HTMLInputElement).type;
  return type !== 'checkbox' && type !== 'radio' && type !== 'button' && type !== 'submit';
};

/** Bottom bar with 4 icon tabs: Queues, Monitor, Alerts, and More. The bar hides while an input has focus. */
export const PhoneTabBar: React.FC<PhoneTabBarProps> = ({ activeArea, onChange }) => {
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

  const activeTab = phoneTabOf(activeArea);

  return (
    <nav
      aria-label="Primary Navigation"
      className={inputFocused ? 'phone-tabbar phone-tabbar--hidden' : 'phone-tabbar'}
    >
      {PHONE_TABS.map((tab) => (
        <button
          key={tab.id}
          type="button"
          className="phone-tabbar__item"
          aria-current={tab.id === activeTab ? 'page' : undefined}
          onClick={() => onChange(tab.id)}
        >
          <Icon name={tab.icon} size={24} />
          <span>{tab.label}</span>
        </button>
      ))}
    </nav>
  );
};
