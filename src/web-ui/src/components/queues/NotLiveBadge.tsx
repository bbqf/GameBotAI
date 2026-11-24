import React from 'react';

/**
 * A small mark that says the device of a running queue is not live (feature 199). It wraps with its text and has no
 * fixed width, so it causes no horizontal scroll on a phone.
 */
export const NotLiveBadge: React.FC = () => <span className="not-live-badge">Not live</span>;
