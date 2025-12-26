import React from 'react';

export type MultiOption = {
  value: string;
  label: string;
};

type MultiSelectProps = {
  id?: string;
  label?: string;
  values: string[];
  options: MultiOption[];
  disabled?: boolean;
  onChange: (values: string[]) => void;
};

export const MultiSelect: React.FC<MultiSelectProps> = ({ id, label, values, options, disabled, onChange }) => {
  return (
    <div className="multiselect-field">
      {label && (
        <label htmlFor={id} className="multiselect-label">
          {label}
        </label>
      )}
      <select
        id={id}
        className="multiselect"
        multiple
        disabled={disabled}
        value={values}
        onChange={(e) => {
          const selected: string[] = Array.from(e.target.selectedOptions).map((o) => o.value);
          onChange(selected);
        }}
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  );
};
