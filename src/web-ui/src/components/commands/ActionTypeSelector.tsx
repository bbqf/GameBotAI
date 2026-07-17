import React from 'react';

export type PrimitiveActionType = 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | 'KeyInput' | 'Swipe' | 'GoToHomeScreen';

export type ActionTypeSelectorProps = {
  value: PrimitiveActionType | '';
  onChange: (next: PrimitiveActionType | '') => void;
  disabled?: boolean;
};

export const ActionTypeSelector: React.FC<ActionTypeSelectorProps> = ({ value, onChange, disabled }) => {
  return (
    <div className="field">
      <label htmlFor="action-type-selector">Action type</label>
      <select
        id="action-type-selector"
        value={value}
        onChange={(e) => onChange(e.target.value as PrimitiveActionType | '')}
        disabled={disabled}
      >
        <option value="">— Select an action —</option>
        <option value="PrimitiveTap">Tap</option>
        <option value="WaitForImage">Wait for Image</option>
        <option value="EnsureGameRunning">Ensure Game Running</option>
        <option value="GoToHomeScreen">Go to Home Screen</option>
        <option value="KeyInput">Key Input</option>
        <option value="Swipe">Swipe</option>
      </select>
    </div>
  );
};
