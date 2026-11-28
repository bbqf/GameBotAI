import React, { useEffect, useId, useRef } from 'react';
import { Button } from './Button';

export type ConfirmDialogProps = {
  open: boolean;
  title: string;
  message: string;
  /** Extra content between the message and the buttons, for example a warning. */
  details?: React.ReactNode;
  confirmText: string;
  cancelText?: string;
  danger?: boolean;
  /** While true, both buttons are off. */
  busy?: boolean;
  /** While true, only the confirm button is off. Cancel stays on. */
  confirmDisabled?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
};

/** A dialog that asks the operator to confirm an action. The focus starts on Cancel, so a stray Enter does no harm. */
export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
  open,
  title,
  message,
  details,
  confirmText,
  cancelText = 'Cancel',
  danger,
  busy,
  confirmDisabled,
  onConfirm,
  onCancel,
}) => {
  const titleId = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);
  const onCancelRef = useRef(onCancel);
  onCancelRef.current = onCancel;

  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement as HTMLElement | null;
    cancelRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancelRef.current();
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previous?.focus?.();
    };
  }, [open]);

  if (!open) return null;

  return (
    <div className="ui-dialog__backdrop">
      <div className="ui-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h3 id={titleId} className="ui-dialog__title">{title}</h3>
        <p className="ui-dialog__message">{message}</p>
        {details && <div className="ui-dialog__details">{details}</div>}
        <div className="ui-dialog__buttons">
          <Button ref={cancelRef} variant="secondary" disabled={busy} onClick={onCancel}>{cancelText}</Button>
          <Button variant={danger ? 'danger' : 'primary'} disabled={busy || confirmDisabled} onClick={onConfirm}>{confirmText}</Button>
        </div>
      </div>
    </div>
  );
};