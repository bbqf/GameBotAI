import React from 'react';

export type EnsureGameRunningPanelProps = {
  onConfirm: () => void;
  onCancel: () => void;
  disabled?: boolean;
};

export const EnsureGameRunningPanel: React.FC<EnsureGameRunningPanelProps> = ({
  onConfirm,
  onCancel,
  disabled,
}) => {
  return (
    <div className="action-panel action-panel--ensure-game-running">
      <p className="action-panel__description">
        Checks that the game is in the foreground; starts it if not running.
      </p>
      <div className="action-panel__controls">
        <button type="button" onClick={onConfirm} disabled={disabled}>
          Add
        </button>
        <button type="button" onClick={onCancel} disabled={disabled}>
          Cancel
        </button>
      </div>
    </div>
  );
};
