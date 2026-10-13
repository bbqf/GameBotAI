import React, { useEffect, useRef } from 'react';

export type RestartEmulatorDialogProps = {
  /** The name of the emulator, for example "pns (emulator-5558)". */
  emulatorName: string;
  /** The name of the queue that uses the emulator, or null. A queue id with no name still shows the warning. */
  queueName: string | null;
  hasQueue: boolean;
  onCancel: () => void;
  onConfirm: () => void;
};

/**
 * Confirm dialog for an emulator restart (feature 141, contract M-31 and M-32). Escape and "Cancel" close it and
 * send nothing. Focus starts on "Cancel" and stays inside the dialog.
 */
export const RestartEmulatorDialog: React.FC<RestartEmulatorDialogProps> = ({
  emulatorName,
  queueName,
  hasQueue,
  onCancel,
  onConfirm
}) => {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      onCancel();
      return;
    }
    if (event.key !== 'Tab') return;
    const first = cancelRef.current;
    const last = confirmRef.current;
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="Restart emulator" onKeyDown={onKeyDown}>
      <div className="modal">
        <h3>Restart emulator</h3>
        <p>
          Restart the emulator <strong>{emulatorName}</strong>? The whole emulator stops and starts again.
        </p>
        {hasQueue && (
          <p role="note" data-testid="restart-queue-warning">
            Queue {queueName ?? '(no name)'} uses this emulator. The queue work will stop for a short time.
          </p>
        )}
        <div className="modal-actions">
          <button type="button" ref={cancelRef} className="btn btn-secondary" onClick={onCancel}>Cancel</button>
          <button type="button" ref={confirmRef} className="btn btn-danger" onClick={onConfirm}>Restart</button>
        </div>
      </div>
    </div>
  );
};
