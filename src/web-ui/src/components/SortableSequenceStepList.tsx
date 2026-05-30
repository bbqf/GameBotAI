import React from 'react';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { SortableStepItem } from './SortableStepItem';
import { DropIndicator, dropIndicatorBefore } from './DropIndicator';
import type { ReorderableListItem } from './ReorderableList';

type SortableSequenceStepListProps = {
  items: ReorderableListItem[];
  onDelete: (item: ReorderableListItem) => void;
  disabled?: boolean;
  emptyMessage?: string;
  activeId?: string | null;
  overId?: string | null;
};

export const SortableSequenceStepList: React.FC<SortableSequenceStepListProps> = ({
  items,
  onDelete,
  disabled,
  emptyMessage = 'No steps added yet.',
  activeId,
  overId,
}) => {
  const ids = items.map((item) => item.id);
  const indicatorBefore = dropIndicatorBefore(ids, activeId ?? null, overId ?? null);

  if (items.length === 0) {
    return <div className="reorderable-list"><div className="empty-state">{emptyMessage}</div></div>;
  }

  return (
    <div className="reorderable-list">
      <SortableContext items={ids} strategy={verticalListSortingStrategy}>
        {items.map((item, index) => (
          <React.Fragment key={item.id}>
            {indicatorBefore === index && <DropIndicator />}
            <div className="reorderable-list__item">
              <SortableStepItem id={item.id} scopeId="root" disabled={disabled}>
                <div className="reorderable-list__content">
                  <div className="reorderable-list__label">{item.label}</div>
                  {item.description && <div className="reorderable-list__description">{item.description}</div>}
                  {item.details && <div className="reorderable-list__details">{item.details}</div>}
                </div>
                <div className="reorderable-list__controls">
                  <button type="button" onClick={() => onDelete(item)} disabled={disabled}>Delete</button>
                </div>
              </SortableStepItem>
            </div>
          </React.Fragment>
        ))}
        {indicatorBefore === items.length && <DropIndicator />}
      </SortableContext>
    </div>
  );
};
