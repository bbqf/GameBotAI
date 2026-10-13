import React from 'react';
import { NavigationArea, NavigationAreaId } from '../types/navigation';

export type NavigationProps = {
  areas: NavigationArea[];
  activeArea: NavigationAreaId;
  onChange: (id: NavigationAreaId) => void;
  isAreaVisible?: (id: NavigationAreaId) => boolean;
};

/** Desktop navigation tabs. The phone layout uses the bottom tab bar instead. */
export const Navigation: React.FC<NavigationProps> = ({ areas, activeArea, onChange, isAreaVisible }) => {
  const sorted = [...areas]
    .filter((area) => (isAreaVisible ? isAreaVisible(area.id) : true))
    .sort((a, b) => a.order - b.order);

  return (
    <nav aria-label="Primary Navigation" className="top-nav">
      <ul className="tabs" role="tablist">
        {sorted.map((area) => {
          const isActive = area.id === activeArea;
          return (
            <li key={area.id} role="presentation">
              <button
                role="tab"
                aria-selected={isActive}
                aria-controls={`${area.id}-panel`}
                className={isActive ? 'tab active' : 'tab'}
                onClick={() => onChange(area.id)}
              >
                {area.label}
              </button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
};
