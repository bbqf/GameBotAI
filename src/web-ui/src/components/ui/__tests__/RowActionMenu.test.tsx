import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { RowActionMenu } from '../RowActionMenu';
import type { RowAction } from '../types';

const make = (over: Partial<RowAction> & { id: string }): RowAction => ({ label: over.id, icon: 'edit', onSelect: jest.fn(), ...over });

const renderMenu = (actions: RowAction[]) => render(<RowActionMenu label="More actions for Daily" actions={actions} />);
const button = () => screen.getByRole('button', { name: 'More actions for Daily' });

describe('RowActionMenu', () => {
  it('has aria-haspopup and aria-expanded on the button', () => {
    renderMenu([make({ id: 'a' })]);
    expect(button()).toHaveAttribute('aria-haspopup', 'menu');
    expect(button()).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(button());
    expect(button()).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('closes with a second click on the button', () => {
    renderMenu([make({ id: 'a' })]);
    fireEvent.click(button());
    fireEvent.click(button());
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('moves the focus to the first item that is on', () => {
    renderMenu([make({ id: 'a', disabled: true }), make({ id: 'b' }), make({ id: 'c' })]);
    fireEvent.click(button());
    expect(screen.getByRole('menuitem', { name: 'b' })).toHaveFocus();
  });

  it('moves the focus with ArrowDown and ArrowUp and skips items that are off', () => {
    renderMenu([make({ id: 'a' }), make({ id: 'b', disabled: true }), make({ id: 'c' })]);
    fireEvent.click(button());
    const menu = screen.getByRole('menu');
    expect(screen.getByRole('menuitem', { name: 'a' })).toHaveFocus();
    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    expect(screen.getByRole('menuitem', { name: 'c' })).toHaveFocus();
    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    expect(screen.getByRole('menuitem', { name: 'a' })).toHaveFocus();
    fireEvent.keyDown(menu, { key: 'ArrowUp' });
    expect(screen.getByRole('menuitem', { name: 'c' })).toHaveFocus();
  });

  it('selects an item with a click, calls it, and closes', () => {
    const onSelect = jest.fn();
    renderMenu([make({ id: 'a', onSelect })]);
    fireEvent.click(button());
    fireEvent.click(screen.getByRole('menuitem', { name: 'a' }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('closes with Escape and gives the focus back to the button', () => {
    renderMenu([make({ id: 'a' })]);
    fireEvent.click(button());
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    expect(screen.queryByRole('menu')).toBeNull();
    expect(button()).toHaveFocus();
  });

  it('closes with Tab', () => {
    renderMenu([make({ id: 'a' })]);
    fireEvent.click(button());
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Tab' });
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('closes with a click outside, a scroll, and a resize', () => {
    renderMenu([make({ id: 'a' })]);
    fireEvent.click(button());
    fireEvent.mouseDown(document.body);
    expect(screen.queryByRole('menu')).toBeNull();
    fireEvent.click(button());
    fireEvent.scroll(window);
    expect(screen.queryByRole('menu')).toBeNull();
    fireEvent.click(button());
    fireEvent.resize(window);
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('stays open for a press inside the menu', () => {
    renderMenu([make({ id: 'a' })]);
    fireEvent.click(button());
    fireEvent.mouseDown(screen.getByRole('menuitem', { name: 'a' }));
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('puts a danger item last, with the danger class, even when it is first', () => {
    renderMenu([make({ id: 'del', tone: 'danger' }), make({ id: 'dup' })]);
    fireEvent.click(button());
    const items = screen.getAllByRole('menuitem');
    expect(items.map((i) => i.textContent)).toEqual(['dup', 'del']);
    expect(items[1]).toHaveClass('ui-menu__item--danger');
  });

  it('does nothing for an item that is off', () => {
    const onSelect = jest.fn();
    renderMenu([make({ id: 'a', disabled: true, onSelect })]);
    fireEvent.click(button());
    const item = screen.getByRole('menuitem', { name: 'a' });
    expect(item).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(item);
    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  describe('near the bottom of the window', () => {
    const originalHeight = window.innerHeight;
    afterEach(() => {
      Object.defineProperty(window, 'innerHeight', { value: originalHeight, configurable: true });
    });

    const mockRect = (top: number, bottom: number) => {
      jest.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
        top, bottom, left: 800, right: 836, width: 36, height: 36, x: 800, y: top, toJSON: () => ({}),
      } as DOMRect);
    };

    afterEach(() => jest.restoreAllMocks());

    it('opens upward when the space below is too small', () => {
      Object.defineProperty(window, 'innerHeight', { value: 600, configurable: true });
      mockRect(560, 596);
      renderMenu([make({ id: 'a' }), make({ id: 'b' })]);
      fireEvent.click(button());
      const menu = screen.getByRole('menu');
      expect(menu).toHaveClass('ui-menu--up');
      expect(parseFloat(menu.style.top)).toBeLessThan(560);
    });

    it('opens downward when there is space', () => {
      Object.defineProperty(window, 'innerHeight', { value: 800, configurable: true });
      mockRect(100, 136);
      renderMenu([make({ id: 'a' }), make({ id: 'b' })]);
      fireEvent.click(button());
      const menu = screen.getByRole('menu');
      expect(menu).not.toHaveClass('ui-menu--up');
      expect(parseFloat(menu.style.top)).toBeGreaterThan(136);
    });
  });
});