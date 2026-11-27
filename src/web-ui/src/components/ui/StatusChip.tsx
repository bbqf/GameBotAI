import React from 'react';

export type ChipState = 'running' | 'paused' | 'stopped' | 'failed' | 'done' | 'unknown';

export type StatusChipProps = {
  state: ChipState;
  /** The word in the chip. The default is the state name with a capital letter. */
  text?: string;
  testId?: string;
};

const defaultText = (state: ChipState): string => state.charAt(0).toUpperCase() + state.slice(1);

/** A status chip: a dot and one word. The color comes from the state. */
export const StatusChip: React.FC<StatusChipProps> = ({ state, text, testId }) => {
  const label = text ?? defaultText(state);
  return (
    <span className={`ui-chip ui-chip--${state}`} role="status" aria-label={`Status: ${label}`} data-testid={testId}>
      <span className="ui-chip__dot" aria-hidden="true" />
      <span className="ui-chip__text">{label}</span>
    </span>
  );
};