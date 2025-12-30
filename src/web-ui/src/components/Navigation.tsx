import React from 'react';
import { NavigationArea, NavigationAreaId } from '../types/navigation';

export type NavigationProps = {
  areas: NavigationArea[];
  activeArea: NavigationAreaId;
  onChange: (id: NavigationAreaId) => void;
  isCollapsed: boolean;
};

export const Navigation: React.FC<NavigationProps> = ({ areas, activeArea, onChange, isCollapsed }) => {
  const sorted = [...areas].sort((a, b) => a.order - b.order);

  if (isCollapsed) {
    return (
      <nav aria-label="Primary Navigation" className="top-nav top-nav--collapsed">
        <label htmlFor="nav-select" className="sr-only">Navigation</label>
        <select
          id="nav-select"
          aria-label="Navigation menu"
          value={activeArea}
          onChange={(e) => onChange(e.target.value as NavigationAreaId)}
        >
          {sorted.map((area) => (
            <option key={area.id} value={area.id}>{area.label}</option>
          ))}
        </select>
      </nav>
    );
  }

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