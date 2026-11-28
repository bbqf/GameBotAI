import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { AppBar } from '../AppBar';
import type { MainTabId } from '../../types/navigation';

const renderBar = (activeTab: MainTabId = 'queues', connectionState: 'online' | 'lost' = 'online', onSelect = jest.fn()) => {
  render(<AppBar activeTab={activeTab} onSelect={onSelect} connectionState={connectionState} />);
  return onSelect;
};

describe('AppBar', () => {
  it('shows the brand GameBot and not the old name', () => {
    renderBar();
    expect(screen.getByText('GameBot')).toBeInTheDocument();
    expect(screen.queryByText('GameBot Web UI')).not.toBeInTheDocument();
  });

  it('shows 5 tabs in order, each with one icon', () => {
    renderBar();
    const nav = screen.getByRole('navigation', { name: 'Primary Navigation' });
    expect(nav).toBeInTheDocument();
    const tabs = screen.getAllByRole('tab');
    expect(tabs.map((t) => t.textContent)).toEqual(['Queues', 'Monitor', 'Alerts', 'Authoring', 'Settings']);
    tabs.forEach((tab) => expect(tab.querySelectorAll('svg')).toHaveLength(1));
  });

  it('marks only the active tab', () => {
    renderBar('notifications');
    expect(screen.getByRole('tab', { name: 'Alerts' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Queues' })).toHaveAttribute('aria-selected', 'false');
  });

  it('calls onSelect with the tab id, also for the active tab', () => {
    const onSelect = renderBar('queues');
    fireEvent.click(screen.getByRole('tab', { name: 'Monitor' }));
    expect(onSelect).toHaveBeenLastCalledWith('monitoring');
    fireEvent.click(screen.getByRole('tab', { name: 'Settings' }));
    expect(onSelect).toHaveBeenLastCalledWith('settings');
    fireEvent.click(screen.getByRole('tab', { name: 'Queues' }));
    expect(onSelect).toHaveBeenLastCalledWith('queues');
  });

  it('shows Online when the connection is online', () => {
    renderBar('queues', 'online');
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Online');
    expect(status).toHaveClass('ui-appbar__conn--online');
  });

  it('shows Connection lost when the connection is lost', () => {
    renderBar('queues', 'lost');
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Connection lost');
    expect(status).toHaveClass('ui-appbar__conn--lost');
  });
});
