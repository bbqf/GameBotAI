import React, { useMemo } from 'react';

export type ListItem = {
  id: string;
  name: string;
  details?: Record<string, string | number | boolean | undefined>;
};

type ListProps = {
  items: ListItem[];
  emptyMessage?: string;
  onSelect?: (id: string) => void;
};

export const List: React.FC<ListProps> = ({ items, emptyMessage = 'No items found.', onSelect }) => {
  const sorted = useMemo(() => {
    return [...items].sort((a, b) => a.name.localeCompare(b.name));
  }, [items]);

  if (!sorted.length) {
    return <div className="empty-state">{emptyMessage}</div>;
  }

  return (
    <div className="list">
      <ul>
        {sorted.map((it) => (
          <li key={it.id} className="list-item">
            <button className="list-row" onClick={() => onSelect?.(it.id)}>
              <div className="list-title">{it.name}</div>
              {it.details && (
                <div className="list-details">
                  {Object.entries(it.details)
                    .filter(([_, v]) => v !== undefined)
                    .slice(0, 3)
                    .map(([k, v]) => (
                      <span key={k} className="detail">
                        <strong>{k}:</strong> {String(v)}
                      </span>
                    ))}
                </div>
              )}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
};
