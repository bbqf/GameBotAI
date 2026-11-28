import React, { useRef } from 'react';

export type SubTabsProps<T extends string> = {
  /** The name of the navigation, for example "Settings Navigation". */
  ariaLabel: string;
  tabs: { id: T; label: string }[];
  active: T;
  /** Called on each click, also on the active tab (the parent can reset the page). */
  onSelect: (id: T) => void;
};

/**
 * The row of sub-tabs under the app bar (desktop). The active tab is in the tab order; the arrow keys, Home, and End
 * move the focus only. Enter and Space select the tab (the native button click).
 */
export function SubTabs<T extends string>({ ariaLabel, tabs, active, onSelect }: SubTabsProps<T>): React.ReactElement {
  const refs = useRef<(HTMLButtonElement | null)[]>([]);

  const moveFocus = (index: number) => {
    const count = tabs.length;
    refs.current[((index % count) + count) % count]?.focus();
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key === 'ArrowRight') moveFocus(index + 1);
    else if (event.key === 'ArrowLeft') moveFocus(index - 1);
    else if (event.key === 'Home') moveFocus(0);
    else if (event.key === 'End') moveFocus(tabs.length - 1);
    else return;
    event.preventDefault();
  };

  return (
    <nav aria-label={ariaLabel} className="ui-subtabs">
      <div role="tablist" className="ui-subtabs__list">
        {tabs.map((tab, index) => {
          const isActive = tab.id === active;
          return (
            <button
              key={tab.id}
              ref={(el) => { refs.current[index] = el; }}
              type="button"
              role="tab"
              aria-selected={isActive}
              tabIndex={isActive ? 0 : -1}
              className="ui-subtabs__tab"
              onClick={() => onSelect(tab.id)}
              onKeyDown={(event) => onKeyDown(event, index)}
            >
              {tab.label}
            </button>
          );
        })}
      </div>
    </nav>
  );
}
