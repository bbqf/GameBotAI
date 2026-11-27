import React from 'react';

export type FooterBarProps = {
  left?: React.ReactNode;
  right: React.ReactNode;
};

/** A bar at the bottom of a form page. The left area holds a secondary action, the right area the main actions. */
export const FooterBar: React.FC<FooterBarProps> = ({ left, right }) => (
  <div className="ui-footer-bar">
    <div className="ui-footer-bar__left">{left}</div>
    <div className="ui-footer-bar__right">{right}</div>
  </div>
);