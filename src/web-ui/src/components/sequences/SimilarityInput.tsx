import React, { useState, useEffect } from 'react';

type SimilarityInputProps = {
  value: number | null | undefined;
  onChange: (value: number | null) => void;
  disabled?: boolean;
  'data-testid'?: string;
  style?: React.CSSProperties;
};

export const SimilarityInput: React.FC<SimilarityInputProps> = ({ value, onChange, disabled, style, ...rest }) => {
  const [text, setText] = useState(() => (value == null ? '' : String(value)));

  useEffect(() => {
    setText(prev => {
      // Don't clobber intermediate typing states that end with a decimal point
      if (/\.$/.test(prev) || prev === '.') return prev;
      const incoming = value == null ? '' : String(value);
      return incoming;
    });
  }, [value]);

  return (
    <input
      type="text"
      inputMode="decimal"
      placeholder="0–1 (default: 0.85)"
      value={text}
      disabled={disabled}
      style={style}
      data-testid={rest['data-testid']}
      onChange={(e) => {
        const raw = e.target.value;
        // Allow empty → null
        if (raw === '') {
          setText('');
          onChange(null);
          return;
        }
        // Allow any partial decimal that could become a valid 0-1 number
        // e.g. ".", "0.", "1.", ".9", "0.8"
        if (/^[01]?\.\d*$|^\.\d*$/.test(raw)) {
          setText(raw);
          const num = Number(raw);
          if (!isNaN(num) && num >= 0 && num <= 1) {
            onChange(num);
          }
          return;
        }
        const num = Number(raw);
        if (!isNaN(num) && num >= 0 && num <= 1) {
          setText(raw);
          onChange(num);
        }
        // else: reject the keystroke (don't update state)
      }}
    />
  );
};
