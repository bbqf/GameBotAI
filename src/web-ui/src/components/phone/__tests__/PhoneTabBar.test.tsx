import React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { PhoneTabBar } from '../PhoneTabBar';
import { NavigationAreaId, navigationAreas } from '../../../types/navigation';

const renderBar = (activeArea: NavigationAreaId = 'queues', onChange = jest.fn()) => {
  render(<><input aria-label="field" /><PhoneTabBar areas={navigationAreas} activeArea={activeArea} onChange={onChange} /></>);
  return onChange;
};

describe('PhoneTabBar', () => {
  it('shows 4 buttons in order, each with one icon, and no Execution', () => {
    renderBar();
    const nav = screen.getByRole('navigation', { name: 'Primary Navigation' });
    const buttons = within(nav).getAllByRole('button');
    expect(buttons.map((b) => b.textContent)).toEqual(['Queues', 'Monitor', 'Alerts', 'More']);
    buttons.forEach((b) => expect(b.querySelectorAll('svg')).toHaveLength(1));
    expect(within(nav).queryByRole('button', { name: 'Execution' })).toBeNull();
  });

  it.each<[NavigationAreaId, string]>([
    ['queues', 'Queues'],
    ['monitoring', 'Monitor'],
    ['notifications', 'Alerts'],
    ['more', 'More'],
    ['configuration', 'More'],
    ['authoring', 'More']
  ])('marks the tab of the area %s as current: %s', (area, name) => {
    renderBar(area);
    const current = screen.getAllByRole('button').filter((b) => b.getAttribute('aria-current') === 'page');
    expect(current.map((b) => b.textContent)).toEqual([name]);
  });

  it('calls onChange for each tab, and More opens the More area', () => {
    const onChange = renderBar();
    fireEvent.click(screen.getByRole('button', { name: 'Monitor' }));
    expect(onChange).toHaveBeenLastCalledWith('monitoring');
    fireEvent.click(screen.getByRole('button', { name: 'Alerts' }));
    expect(onChange).toHaveBeenLastCalledWith('notifications');
    fireEvent.click(screen.getByRole('button', { name: 'More' }));
    expect(onChange).toHaveBeenLastCalledWith('more');
  });

  it('has no More sheet', () => {
    renderBar();
    fireEvent.click(screen.getByRole('button', { name: 'More' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('hides while an input has focus', () => {
    renderBar();
    const nav = screen.getByRole('navigation', { name: 'Primary Navigation' });
    expect(nav).not.toHaveClass('phone-tabbar--hidden');
    act(() => screen.getByLabelText('field').focus());
    expect(nav).toHaveClass('phone-tabbar--hidden');
    act(() => screen.getByLabelText('field').blur());
    expect(nav).not.toHaveClass('phone-tabbar--hidden');
  });
});
