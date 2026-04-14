import React, { useCallback, useMemo, useRef, useState } from 'react';
import { ConfigParameterRow } from './ConfigParameterRow';
import type { ConfigurationParameter } from '../services/config';

export type ConfigParameterListProps = {
  parameters: ConfigurationParameter[];
  onApply?: (updates: Record<string, string | null>) => Promise<void>;
  onReorder?: (orderedKeys: string[]) => Promise<void>;
  applyError?: string | null;
};

export const ConfigParameterList: React.FC<ConfigParameterListProps> = ({
  parameters, onApply, onReorder, applyError
}) => {
  const [edits, setEdits] = useState<Record<string, string>>({});
  const [filter, setFilter] = useState('');
  const [applying, setApplying] = useState(false);
  const [dragIdx, setDragIdx] = useState<number | null>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const handleChange = useCallback((name: string, value: string) => {
    setEdits(prev => ({ ...prev, [name]: value }));
  }, []);

  // Dirty detection: compare edits to original values
  const dirtyKeys = useMemo(() => {
    const dirty = new Set<string>();
    for (const [key, val] of Object.entries(edits)) {
      const orig = parameters.find(p => p.name === key);
      if (!orig) continue;
      const origVal = orig.isSecret ? '' : String(orig.value ?? '');
      if (val !== origVal) dirty.add(key);
    }
    return dirty;
  }, [edits, parameters]);

  const hasDirty = dirtyKeys.size > 0;

  // Filter
  const filterLower = filter.toLowerCase();
  const filtered = useMemo(() => {
    if (!filterLower) return parameters;
    return parameters.filter(p =>
      p.name.toLowerCase().includes(filterLower) ||
      String(p.value ?? '').toLowerCase().includes(filterLower)
    );
  }, [parameters, filterLower]);

  // Apply All
  const handleApply = useCallback(async () => {
    if (!onApply || !hasDirty) return;
    setApplying(true);
    try {
      const updates: Record<string, string | null> = {};
      for (const key of dirtyKeys) {
        updates[key] = edits[key] || null;
      }
      await onApply(updates);
      setEdits({});
    } finally {
      setApplying(false);
    }
  }, [onApply, hasDirty, dirtyKeys, edits]);

  // Drag-and-drop handlers
  const handleDragStart = useCallback((idx: number) => (e: React.DragEvent<HTMLDivElement>) => {
    setDragIdx(idx);
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', String(idx));
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
  }, []);

  const handleDrop = useCallback((targetIdx: number) => async (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    const sourceIdx = dragIdx;
    setDragIdx(null);
    if (sourceIdx === null || sourceIdx === targetIdx || !onReorder) return;

    // Map filtered indices back to full parameter list
    const fullKeys = parameters.map(p => p.name);
    const sourceKey = filtered[sourceIdx]?.name;
    const targetKey = filtered[targetIdx]?.name;
    if (!sourceKey || !targetKey) return;

    const sourceFullIdx = fullKeys.indexOf(sourceKey);
    const targetFullIdx = fullKeys.indexOf(targetKey);
    if (sourceFullIdx === -1 || targetFullIdx === -1) return;

    const newKeys = [...fullKeys];
    newKeys.splice(sourceFullIdx, 1);
    newKeys.splice(targetFullIdx, 0, sourceKey);

    await onReorder(newKeys);
  }, [dragIdx, filtered, parameters, onReorder]);

  const handleDragEnd = useCallback(() => {
    setDragIdx(null);
  }, []);

  return (
    <div className="config-param-list" ref={listRef}>
      <div className="config-param-toolbar">
        <input
          className="config-param-filter"
          type="text"
          placeholder="Filter parameters…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          aria-label="Filter parameters"
        />
        {onApply && (
          <button
            className="config-apply-btn"
            disabled={!hasDirty || applying}
            onClick={handleApply}
          >
            {applying ? 'Applying…' : 'Apply All'}
          </button>
        )}
      </div>
      {applyError && <p className="form-error" role="alert">{applyError}</p>}
      {filtered.length === 0 ? (
        <p className="config-empty-state">No matching parameters</p>
      ) : (
        filtered.map((p, idx) => (
          <ConfigParameterRow
            key={p.name}
            param={p}
            editValue={edits[p.name]}
            isDirty={dirtyKeys.has(p.name)}
            onChange={onApply ? handleChange : undefined}
            draggable={!!onReorder}
            onDragStart={onReorder ? handleDragStart(idx) : undefined}
            onDragOver={onReorder ? handleDragOver : undefined}
            onDrop={onReorder ? handleDrop(idx) : undefined}
            onDragEnd={onReorder ? handleDragEnd : undefined}
          />
        ))
      )}
    </div>
  );
};
