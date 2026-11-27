import React from 'react';
import { ICON_PATHS, type IconName } from './icons';

export type IconProps = {
  name: IconName;
  /** The size in px. The default is 20. */
  size?: number;
};

/** One icon of the shared set (feature 202). It is hidden from screen readers: the button that holds it has the name. */
export const Icon: React.FC<IconProps> = ({ name, size = 20 }) => (
  <svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true" focusable="false">
    <path d={ICON_PATHS[name]} fill="currentColor" />
  </svg>
);