import React, { useMemo, useState } from 'react';
import type { ParameterFieldProps } from './fieldProps';
import { BUTTON_LIMIT, SEARCH_THRESHOLD } from './fieldProps';

/**
 * The choice field of a choice parameter (feature 168). The person sees the readable labels only. The
 * technical value is stored. Six options or fewer show buttons. A longer list shows a drop-down, with a
 * search box above 12 options.
 */
export const ChoiceField: React.FC<ParameterFieldProps> = ({ declaration, value, onChange, disabled, ariaLabel }) => {
  const options = useMemo(() => declaration.options ?? [], [declaration.options]);
  const [search, setSearch] = useState('');
  const current = value ?? '';
  const gone = current !== '' && !options.some((o) => o.value === current);

  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    return term ? options.filter((o) => o.label.toLowerCase().includes(term)) : options;
  }, [options, search]);

  const missingMessage = gone && (
    <p className="parameter-value-message" role="alert">
      This option no longer exists. Select one of the options.
    </p>
  );

  if (options.length <= BUTTON_LIMIT) {
    return (
      <div className="choice-field">
        <div role="radiogroup" aria-label={ariaLabel} className="choice-buttons">
          {options.map((option, index) => (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={option.value === current}
              className={`choice-button${option.value === current ? ' choice-button-selected' : ''}`}
              disabled={disabled}
              tabIndex={option.value === current || (current === '' && index === 0) ? 0 : -1}
              onClick={() => onChange(option.value)}
              onKeyDown={(event) => {
                const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1
                  : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;
                if (step === 0) return;
                event.preventDefault();
                onChange(options[(index + step + options.length) % options.length].value);
              }}
            >
              {option.label}
            </button>
          ))}
        </div>
        {missingMessage}
      </div>
    );
  }

  return (
    <div className="choice-field">
      {options.length > SEARCH_THRESHOLD && (
        <input
          type="search"
          className="choice-search"
          value={search}
          placeholder="Search options"
          aria-label={`Search options for ${declaration.label?.trim() || declaration.name}`}
          onChange={(e) => setSearch(e.target.value)}
        />
      )}
      <select
        className="choice-select"
        value={gone ? '' : current}
        disabled={disabled}
        aria-label={ariaLabel}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="" disabled>
          Select an option
        </option>
        {shown.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      {missingMessage}
    </div>
  );
};
