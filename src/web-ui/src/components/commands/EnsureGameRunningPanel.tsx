import React, { useState } from 'react';

export type EnsureGameRunningPanelProps = {
  /** Called with the value of the "Force restart" option. */
  onConfirm: (forceRestart: boolean) => void;
  onCancel: () => void;
  disabled?: boolean;
  /** The option value when the step is edited. The default is false. */
  initialForceRestart?: boolean;
};

export const EnsureGameRunningPanel: React.FC<EnsureGameRunningPanelProps> = ({
  onConfirm,
  onCancel,
  disabled,
  initialForceRestart = false,
}) => {
  const [forceRestart, setForceRestart] = useState(initialForceRestart);

  return (
    <div className="action-panel action-panel--ensure-game-running">
      <p className="action-panel__description">
        Checks that the game is in the foreground; starts it if not running.
      </p>
      <label className="action-panel__field">
        <input
          type="checkbox"
          checked={forceRestart}
          onChange={(e) => setForceRestart(e.target.checked)}
          disabled={disabled}
        />{' '}
        Force restart
      </label>
      <p className="action-panel__hint">
        Stops the game and starts it again, even when it is running.
      </p>
      <div className="action-panel__controls">
        <button type="button" onClick={() => onConfirm(forceRestart)} disabled={disabled}>
          Add
        </button>
        <button type="button" onClick={onCancel} disabled={disabled}>
          Cancel
        </button>
      </div>
    </div>
  );
};
