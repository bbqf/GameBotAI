import React from 'react';

export type Option = {
  value: string;
  label: string;
};

type DropdownProps = {
  id?: string;
  label?: string;
  value?: string;
  options: Option[];
  placeholder?: string;
  disabled?: boolean;
  onChange: (value: string | undefined) => void;
  error?: string;
  describedById?: string;
};

export const Dropdown: React.FC<DropdownProps> = ({
  id,
  label,
  value,
  options,
  placeholder = 'Select…',
  disabled,
  onChange,
  error,
  describedById,
}) => {
  return (
    <div className="dropdown-field">
      {label && (
        <label htmlFor={id} className="dropdown-label">
          {label}
        </label>
      )}
      <select
        id={id}
        className="dropdown"
        value={value ?? ''}
        disabled={disabled}
        aria-invalid={!!error}
        aria-describedby={describedById}
        onChange={(e) => {
          const val = e.target.value;
          onChange(val === '' ? undefined : val);
        }}
      >
        <option value="">{placeholder}</option>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      {error && (
        <div id={describedById} className="field-error" role="alert">
          {error}
        </div>
      )}
    </div>
  );
};
