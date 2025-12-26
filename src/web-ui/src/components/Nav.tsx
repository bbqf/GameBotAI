import React from 'react';

export type AuthoringTab = 'Actions' | 'Commands' | 'Games' | 'Sequences' | 'Triggers';

type NavProps = {
  active: AuthoringTab;
  onChange: (tab: AuthoringTab) => void;
};

const tabs: AuthoringTab[] = ['Actions', 'Commands', 'Games', 'Sequences', 'Triggers'];

export const Nav: React.FC<NavProps> = ({ active, onChange }) => {
  return (
    <nav className="authoring-nav" aria-label="Authoring Navigation">
      <ul className="tabs" role="tablist">
        {tabs.map((t) => (
          <li key={t} role="presentation">
            <button
              role="tab"
              aria-selected={active === t}
              className={active === t ? 'tab active' : 'tab'}
              onClick={() => onChange(t)}
            >
              {t}
            </button>
          </li>
        ))}
      </ul>
    </nav>
  );
};
