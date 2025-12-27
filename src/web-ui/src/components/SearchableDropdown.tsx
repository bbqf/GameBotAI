import React, { useMemo, useState } from 'react';

export type SearchableOption = {
  value: string;
  label: string;
  description?: string;
};

type SearchableDropdownProps = {
  id?: string;
  label?: string;
  value?: string;
  options: SearchableOption[];
  placeholder?: string;
  searchPlaceholder?: string;
  disabled?: boolean;
  onChange: (value: string | undefined) => void;
  onCreateNew?: () => void;
  createLabel?: string;
  error?: string;
};

export const SearchableDropdown: React.FC<SearchableDropdownProps> = ({
  id,
  label,
  value,
  options,
  placeholder = 'Select…',
  searchPlaceholder = 'Search…',
  disabled,
  onChange,
  onCreateNew,
  createLabel = 'Create new',
  error,
}) => {
  const [query, setQuery] = useState('');

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter((opt) =>
      opt.label.toLowerCase().includes(q) || (opt.description ? opt.description.toLowerCase().includes(q) : false)
    );
  }, [options, query]);

  const describedById = error ? `${id ?? 'searchable-dropdown'}-error` : undefined;

  return (
    <div className="searchable-dropdown">
      {label && <label htmlFor={id} className="dropdown-label">{label}</label>}
      <input
        aria-label={label ? `${label} search` : 'Search options'}
        className="dropdown-search"
        placeholder={searchPlaceholder}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        disabled={disabled}
      />
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
        {filtered.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
      {onCreateNew && (
        <div className="dropdown-create">
          <button type="button" onClick={onCreateNew} disabled={disabled}>{createLabel}</button>
        </div>
      )}
      {error && (
        <div id={describedById} className="field-error" role="alert">{error}</div>
      )}
    </div>
  );
};
