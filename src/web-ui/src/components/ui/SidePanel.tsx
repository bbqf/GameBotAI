import React, { useEffect, useId, useRef } from 'react';
import { useLayoutMode, type LayoutMode } from '../../hooks/useLayoutMode';
import { IconButton } from './IconButton';

export type SidePanelProps = {
  open: boolean;
  title: string;
  onClose: () => void;
  /** The default comes from the window size. */
  mode?: LayoutMode;
  testId?: string;
  children?: React.ReactNode;
};

const FOCUSABLE = 'input, select, textarea, button, [href], [tabindex]:not([tabindex="-1"])';

/**
 * A panel for a form. On the desktop it is fixed at the right edge and the page stays visible.
 * On the phone it is a full page with a "Back" button.
 */
export const SidePanel: React.FC<SidePanelProps> = ({ open, title, onClose, mode, testId, children }) => {
  const layout = useLayoutMode();
  const phone = (mode ?? layout) === 'phone';
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const bodyRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const opener = document.activeElement as HTMLElement | null;
    const first = bodyRef.current?.querySelector<HTMLElement>(FOCUSABLE);
    (first ?? panelRef.current)?.focus();
    return () => {
      if (opener && document.contains(opener)) opener.focus();
    };
  }, [open]);

  if (!open) return null;

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      onClose();
    }
  };

  const header = phone ? (
    <div className="ui-side-panel__header">
      <IconButton icon="back" label="Back" size="touch" onClick={onClose} />
      <h2 id={titleId} className="ui-side-panel__title">{title}</h2>
    </div>
  ) : (
    <div className="ui-side-panel__header">
      <h2 id={titleId} className="ui-side-panel__title">{title}</h2>
      <IconButton icon="close" label="Close" onClick={onClose} />
    </div>
  );

  // One element for both modes keeps the focus logic simple. The tag differs only by the mode class.
  return (
    // The panel is a dialog. Escape inside it closes it.
    // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions
    <div
      ref={panelRef}
      className={`ui-side-panel${phone ? ' ui-side-panel--page' : ''}`}
      role="dialog"
      aria-modal="false"
      aria-labelledby={titleId}
      tabIndex={-1}
      data-testid={testId}
      onKeyDown={onKeyDown}
    >
      {header}
      <div ref={bodyRef} className="ui-side-panel__body">{children}</div>
    </div>
  );
};
