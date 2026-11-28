import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { SubTabs } from '../SubTabs';

const tabs = [
  { id: 'a', label: 'Alpha' },
  { id: 'b', label: 'Beta' },
  { id: 'c', label: 'Gamma' }
];

const renderTabs = (active = 'a', onSelect = jest.fn()) => {
  render(<SubTabs ariaLabel="Test Navigation" tabs={tabs} active={active} onSelect={onSelect} />);
  return onSelect;
};

describe('SubTabs', () => {
  it('shows the tabs in order inside a tablist with the given label', () => {
    renderTabs();
    expect(screen.getByRole('navigation', { name: 'Test Navigation' })).toBeInTheDocument();
    expect(screen.getByRole('tablist')).toBeInTheDocument();
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Alpha', 'Beta', 'Gamma']);
  });

  it('marks the active tab and puts only it in the tab order', () => {
    renderTabs('b');
    const [a, b, c] = screen.getAllByRole('tab');
    expect(b).toHaveAttribute('aria-selected', 'true');
    expect(a).toHaveAttribute('aria-selected', 'false');
    expect(b).toHaveAttribute('tabindex', '0');
    expect(a).toHaveAttribute('tabindex', '-1');
    expect(c).toHaveAttribute('tabindex', '-1');
  });

  it('calls onSelect with the id, also for the active tab', () => {
    const onSelect = renderTabs('a');
    fireEvent.click(screen.getByRole('tab', { name: 'Beta' }));
    expect(onSelect).toHaveBeenLastCalledWith('b');
    fireEvent.click(screen.getByRole('tab', { name: 'Alpha' }));
    expect(onSelect).toHaveBeenLastCalledWith('a');
    expect(onSelect).toHaveBeenCalledTimes(2);
  });

  it('moves the focus with ArrowRight and ArrowLeft (with wrap) and does not select', () => {
    const onSelect = renderTabs('a');
    const [a, b, c] = screen.getAllByRole('tab');
    a.focus();
    fireEvent.keyDown(a, { key: 'ArrowRight' });
    expect(b).toHaveFocus();
    fireEvent.keyDown(b, { key: 'ArrowRight' });
    expect(c).toHaveFocus();
    fireEvent.keyDown(c, { key: 'ArrowRight' });
    expect(a).toHaveFocus();
    fireEvent.keyDown(a, { key: 'ArrowLeft' });
    expect(c).toHaveFocus();
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('moves the focus to the first and last tab with Home and End', () => {
    const onSelect = renderTabs('b');
    const [a, b, c] = screen.getAllByRole('tab');
    b.focus();
    fireEvent.keyDown(b, { key: 'End' });
    expect(c).toHaveFocus();
    fireEvent.keyDown(c, { key: 'Home' });
    expect(a).toHaveFocus();
    expect(onSelect).not.toHaveBeenCalled();
  });
});
