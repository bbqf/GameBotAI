import React from 'react';
import { Button } from '../ui/Button';

export type DesktopOnlyNoticeProps = {
  areaLabel: string;
  onBack: () => void;
};

/** Notice for an area that does not fit a phone (contract C-04). The area content is never rendered. */
export const DesktopOnlyNotice: React.FC<DesktopOnlyNoticeProps> = ({ areaLabel, onBack }) => (
  <section className="phone-notice" aria-labelledby="desktop-only-heading">
    <h2 id="desktop-only-heading">{areaLabel}</h2>
    <p>Use a larger screen for this area.</p>
    <Button variant="secondary" onClick={onBack}>Back to More</Button>
  </section>
);
