import React from 'react';
import { Icon } from '../ui/Icon';
import { PageHeader } from '../ui/PageHeader';
import { MORE_DESKTOP_ONLY_ROWS, MORE_ROWS, NavigationAreaId, navigationAreas } from '../../types/navigation';

export type MorePageProps = {
  onOpen: (id: NavigationAreaId) => void;
};

const areaOf = (id: NavigationAreaId) => navigationAreas.find((a) => a.id === id);

/** The More page of the phone: rows for the areas that fit a phone, and dimmed rows for the desktop-only areas. */
export const MorePage: React.FC<MorePageProps> = ({ onOpen }) => (
  <section className="phone-more-page">
    <PageHeader title="More" />
    <ul className="phone-more">
      {MORE_ROWS.map((id) => {
        const area = areaOf(id);
        if (!area) return null;
        return (
          <li key={id}>
            <button type="button" className="phone-more__row" onClick={() => onOpen(id)}>
              <Icon name={area.icon} size={22} />
              <span className="phone-more__name">{area.label}</span>
              <Icon name="open" size={22} />
            </button>
          </li>
        );
      })}
    </ul>
    <p className="phone-more__group">Desktop only</p>
    <ul className="phone-more">
      {MORE_DESKTOP_ONLY_ROWS.map((id) => {
        const area = areaOf(id);
        if (!area) return null;
        return (
          <li key={id}>
            <div className="phone-more__row phone-more__row--off" aria-disabled="true">
              <Icon name={area.icon} size={22} />
              <span className="phone-more__name">{area.label}</span>
              <span className="phone-more__status">Use a computer</span>
            </div>
          </li>
        );
      })}
    </ul>
  </section>
);
