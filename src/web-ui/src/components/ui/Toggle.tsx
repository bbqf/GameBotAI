import React, { useId } from 'react';

export type ToggleProps = {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  id?: string;
  disabled?: boolean;
};

/** An on/off switch with a text label. A click on the switch or on the label changes the value. */
export const Toggle: React.FC<ToggleProps> = ({ checked, onChange, label, id, disabled }) => {
  const generated = useId();
  const switchId = id ?? generated;
  const labelId = `${switchId}-label`;
  const change = () => {
    if (!disabled) onChange(!checked);
  };
  return (
    <span className={`ui-toggle${disabled ? ' ui-toggle--disabled' : ''}`}>
      <button
        type="button"
        role="switch"
        id={switchId}
        className={`ui-toggle__switch${checked ? ' ui-toggle__switch--on' : ''}`}
        aria-checked={checked}
        aria-labelledby={labelId}
        disabled={disabled}
        onClick={change}
      >
        <span className="ui-toggle__knob" />
      </button>
      {/* The keyboard path is the switch button, so the label needs no key handler. */}
      {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
      <span id={labelId} className="ui-toggle__label" onClick={change}>{label}</span>
    </span>
  );
};
