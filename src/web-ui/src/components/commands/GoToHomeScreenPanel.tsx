import React from 'react';

export type GoToHomeScreenPanelProps = {
  onConfirm: () => void;
  onCancel: () => void;
  disabled?: boolean;
};

export const GoToHomeScreenPanel: React.FC<GoToHomeScreenPanelProps> = ({
  onConfirm,
  onCancel,
  disabled,
}) => {
  return (
    <div className="action-panel action-panel--go-to-home-screen">
      <p className="action-panel__description">
        Presses the Android HOME button to return to the home screen. The game is left running in the background.
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
