import React, { useMemo, useState } from 'react';

export type ListItem = {
  id: string;
  name: string;
  details?: Record<string, string | number | boolean | undefined>;
};

type ListProps = {
  items: ListItem[];
  emptyMessage?: string;
  onSelect?: (id: string) => void;
  enableFilterThreshold?: number; // show filter input when items exceed threshold (default 50)
};

export const List: React.FC<ListProps> = ({ items, emptyMessage = 'No items found.', onSelect, enableFilterThreshold = 50 }) => {
  const [query, setQuery] = useState('');
  const sorted = useMemo(() => {
    return [...items].sort((a, b) => a.name.localeCompare(b.name));
  }, [items]);
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return sorted;
    return sorted.filter((it) => it.name.toLowerCase().includes(q));
  }, [sorted, query]);

  if (!sorted.length) {
    return <div className="empty-state">{emptyMessage}</div>;
  }

  return (
    <div className="list">
      {sorted.length > enableFilterThreshold && (
        <div className="list-filter">
          <label htmlFor="list-filter-input">Filter</label>
          <input id="list-filter-input" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Type to filter by name" />
        </div>
      )}
      <ul>
        {filtered.map((it) => (
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
