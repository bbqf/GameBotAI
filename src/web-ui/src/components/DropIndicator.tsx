import React from 'react';

export const DropIndicator: React.FC = () => (
  <div className="drop-indicator" aria-hidden="true" />
);

/** Returns the index BEFORE which the indicator line should be rendered.
 *  Returns null when no drag is active or the item is hovering itself. */
export const dropIndicatorBefore = (
  ids: string[],
  activeId: string | null,
  overId: string | null,
): number | null => {
  if (!activeId || !overId || activeId === overId) return null;
  const activeIndex = ids.indexOf(activeId);
  const overIndex = ids.indexOf(overId);
  if (activeIndex === -1 || overIndex === -1) return null;
  // dragging down → item lands after the over item; dragging up → before
  return activeIndex < overIndex ? overIndex + 1 : overIndex;
};
