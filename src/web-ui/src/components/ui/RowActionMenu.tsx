import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Icon } from './Icon';
import { IconButton } from './IconButton';
import type { RowAction } from './types';

export type RowActionMenuProps = {
  /** The accessible name of the "more" button. */
  label: string;
  actions: RowAction[];
  size?: 'default' | 'touch';
};

const ITEM_HEIGHT = 44;
const MENU_PADDING = 8;
const MENU_WIDTH = 200;
const GAP = 4;

type MenuPosition = { top: number; left: number; up: boolean };

/** The "more" button of a row and its menu. A danger item is last. The menu does not leave the window. */
export const RowActionMenu: React.FC<RowActionMenuProps> = ({ label, actions, size = 'default' }) => {
  const [position, setPosition] = useState<MenuPosition | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLUListElement>(null);
  const open = position !== null;
  const items = [...actions.filter((a) => a.tone !== 'danger'), ...actions.filter((a) => a.tone === 'danger')];

  const close = useCallback((restoreFocus: boolean) => {
    setPosition(null);
    if (restoreFocus) buttonRef.current?.focus();
  }, []);

  const toggle = () => {
    if (open) {
      close(false);
      return;
    }
    const rect = buttonRef.current?.getBoundingClientRect();
    if (!rect) return;
    const menuHeight = items.length * ITEM_HEIGHT + MENU_PADDING;
    const up = window.innerHeight - rect.bottom < menuHeight + GAP;
    const top = up ? rect.top - menuHeight - GAP : rect.bottom + GAP;
    const left = Math.max(GAP, rect.right - MENU_WIDTH);
    setPosition({ top, left, up });
  };

  // The focus goes to the first item that is on when the menu opens.
  useEffect(() => {
    if (!open) return;
    const first = menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]:not([aria-disabled="true"])');
    first?.focus();
  }, [open]);

  // A click outside, a scroll, or a resize closes the menu.
  useEffect(() => {
    if (!open) return;
    const onOutside = (event: Event) => {
      const target = event.target as Node | null;
      if (target && (menuRef.current?.contains(target) || buttonRef.current?.contains(target))) return;
      close(false);
    };
    const onViewport = () => close(false);
    document.addEventListener('mousedown', onOutside);
    document.addEventListener('touchstart', onOutside);
    window.addEventListener('scroll', onViewport, true);
    window.addEventListener('resize', onViewport);
    return () => {
      document.removeEventListener('mousedown', onOutside);
      document.removeEventListener('touchstart', onOutside);
      window.removeEventListener('scroll', onViewport, true);
      window.removeEventListener('resize', onViewport);
    };
  }, [open, close]);

  const move = (step: 1 | -1) => {
    const enabled = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not([aria-disabled="true"])') ?? []);
    if (enabled.length === 0) return;
    const index = enabled.indexOf(document.activeElement as HTMLElement);
    const next = index === -1 ? (step === 1 ? 0 : enabled.length - 1) : (index + step + enabled.length) % enabled.length;
    enabled[next].focus();
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      move(1);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      move(-1);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      close(true);
    } else if (event.key === 'Tab') {
      close(false);
    }
  };

  const select = (action: RowAction) => {
    if (action.disabled) return;
    close(true);
    action.onSelect();
  };

  return (
    <>
      <IconButton
        ref={buttonRef}
        icon="more"
        label={label}
        size={size}
        ariaHasPopup="menu"
        ariaExpanded={open}
        onClick={toggle}
      />
      {open && position && (
        <ul
          ref={menuRef}
          role="menu"
          aria-label={label}
          className={`ui-menu${position.up ? ' ui-menu--up' : ''}`}
          style={{ top: position.top, left: position.left }}
          onKeyDown={onKeyDown}
        >
          {items.map((action) => (
            <li key={action.id} role="none">
              <button
                type="button"
                role="menuitem"
                aria-disabled={action.disabled ? 'true' : undefined}
                className={`ui-menu__item${action.tone === 'danger' ? ' ui-menu__item--danger' : ''}`}
                onClick={() => select(action)}
              >
                <Icon name={action.icon} size={18} />
                <span>{action.label}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </>
  );
};