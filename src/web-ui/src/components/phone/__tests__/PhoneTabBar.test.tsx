import React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { PhoneTabBar } from '../PhoneTabBar';
import { navigationAreas } from '../../../types/navigation';

const renderBar = (activeArea = 'queues' as const, onChange = jest.fn()) => {
  render(<><input aria-label="field" /><PhoneTabBar areas={navigationAreas} activeArea={activeArea} onChange={onChange} /></>);
  return onChange;
};

describe('PhoneTabBar', () => {
  it('shows Queues, Monitoring and More, and no Execution', () => {
    renderBar();
    const nav = screen.getByRole('navigation', { name: 'Primary Navigation' });
    expect(within(nav).getByRole('button', { name: 'Queues' })).toBeInTheDocument();
    expect(within(nav).queryByRole('button', { name: 'Execution' })).toBeNull();
    expect(within(nav).getByRole('button', { name: 'Monitoring' })).toBeInTheDocument();
    expect(within(nav).getByRole('button', { name: /More/ })).toBeInTheDocument();
  });

  it('marks the current item with aria-current', () => {
    renderBar('monitoring');
    expect(screen.getByRole('button', { name: 'Monitoring' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Queues' })).not.toHaveAttribute('aria-current');
  });

  it('marks More when the area is not a main tab', () => {
    renderBar('configuration');
    expect(screen.getByRole('button', { name: /More/ })).toHaveAttribute('aria-current', 'page');
  });

  it('calls onChange for a main tab', () => {
    const onChange = renderBar();
    fireEvent.click(screen.getByRole('button', { name: 'Monitoring' }));
    expect(onChange).toHaveBeenCalledWith('monitoring');
  });

  it('lists the other areas in the More sheet and labels the desktop-only ones', () => {
    const onChange = renderBar();
    fireEvent.click(screen.getByRole('button', { name: /More/ }));
    const sheet = screen.getByRole('dialog', { name: 'More areas' });
    expect(within(sheet).getByRole('button', { name: 'Configuration' })).toBeInTheDocument();
    expect(within(sheet).getByRole('button', { name: 'Update' })).toBeInTheDocument();
    expect(within(sheet).getByRole('button', { name: /Authoring/ })).toHaveTextContent('Desktop only');
    expect(within(sheet).getByRole('button', { name: /Execution Logs/ })).toHaveTextContent('Desktop only');
    expect(within(sheet).queryByRole('button', { name: /^Execution$/ })).toBeNull();
    expect(within(sheet).getByRole('button', { name: 'Configuration' })).not.toHaveTextContent('Desktop only');
    fireEvent.click(within(sheet).getByRole('button', { name: 'Update' }));
    expect(onChange).toHaveBeenCalledWith('update');
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
