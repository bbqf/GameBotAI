import React from 'react';
import { Icon } from './ui/Icon';
import { MAIN_TABS, MainTabId } from '../types/navigation';
import type { ConnectionState } from '../hooks/useConnectionState';

export type AppBarProps = {
  activeTab: MainTabId;
  onSelect: (tab: MainTabId) => void;
  connectionState: ConnectionState;
};

/** The desktop app bar: the brand, the 5 top tabs, and the connection state. The phone uses the bottom bar instead. */
export const AppBar: React.FC<AppBarProps> = ({ activeTab, onSelect, connectionState }) => {
  const lost = connectionState !== 'online';
  return (
    <header className="ui-appbar">
      <div className="ui-appbar__brand">GameBot</div>
      <nav aria-label="Primary Navigation" className="ui-appbar__nav">
        <div role="tablist" className="ui-appbar__tabs">
          {MAIN_TABS.map((tab) => {
            const isActive = tab.id === activeTab;
            return (
              <button
                key={tab.id}
                type="button"
                role="tab"
                aria-selected={isActive}
                aria-controls={isActive ? `${tab.id}-panel` : undefined}
                className="ui-appbar__tab"
                onClick={() => onSelect(tab.id)}
              >
                <Icon name={tab.icon} size={20} />
                <span>{tab.label}</span>
              </button>
            );
          })}
        </div>
      </nav>
      <span
        role="status"
        className={lost ? 'ui-appbar__conn ui-appbar__conn--lost' : 'ui-appbar__conn ui-appbar__conn--online'}
      >
        <span className="ui-appbar__dot" aria-hidden="true" />
        {lost ? 'Connection lost' : 'Online'}
      </span>
    </header>
  );
};
