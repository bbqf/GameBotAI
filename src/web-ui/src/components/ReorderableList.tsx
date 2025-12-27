import React from 'react';

export type ReorderableListItem = {
  id: string;
  label: string;
  description?: string;
};

type ReorderableListProps = {
  items: ReorderableListItem[];
  onChange: (next: ReorderableListItem[]) => void;
  onAdd?: () => void;
  onEdit?: (item: ReorderableListItem, index: number) => void;
  onDelete?: (item: ReorderableListItem, index: number) => void;
  emptyMessage?: string;
  disabled?: boolean;
};

const move = (items: ReorderableListItem[], from: number, to: number): ReorderableListItem[] => {
  const next = [...items];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
};

export const ReorderableList: React.FC<ReorderableListProps> = ({
  items,
  onChange,
  onAdd,
  onEdit,
  onDelete,
  emptyMessage = 'No items yet.',
  disabled,
}) => {
  const handleDelete = (index: number) => {
    if (disabled) return;
    const target = items[index];
    const next = items.filter((_, i) => i !== index);
    onDelete?.(target, index);
    onChange(next);
  };

  const handleMove = (from: number, to: number) => {
    if (disabled) return;
    if (to < 0 || to >= items.length) return;
    onChange(move(items, from, to));
  };

  if (!items.length) {
    return (
      <div className="reorderable-list">
        <div className="empty-state">{emptyMessage}</div>
        {onAdd && <button type="button" onClick={onAdd} disabled={disabled}>Add item</button>}
      </div>
    );
  }

  return (
    <div className="reorderable-list">
      <ul>
        {items.map((item, index) => (
          <li key={item.id} className="reorderable-list__item">
            <div className="reorderable-list__content">
              <div className="reorderable-list__label">{item.label}</div>
              {item.description && <div className="reorderable-list__description">{item.description}</div>}
            </div>
            <div className="reorderable-list__controls">
              <button type="button" aria-label="Move up" onClick={() => handleMove(index, index - 1)} disabled={disabled || index === 0}>↑</button>
              <button type="button" aria-label="Move down" onClick={() => handleMove(index, index + 1)} disabled={disabled || index === items.length - 1}>↓</button>
              {onEdit && <button type="button" onClick={() => onEdit(item, index)} disabled={disabled}>Edit</button>}
              <button type="button" onClick={() => handleDelete(index)} disabled={disabled}>Delete</button>
            </div>
          </li>
        ))}
      </ul>
      {onAdd && <button type="button" onClick={onAdd} disabled={disabled}>Add item</button>}
    </div>
  );
};
