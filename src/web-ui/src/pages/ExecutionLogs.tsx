import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ExecutionLogEntryDto,
  ExecutionLogListSortBy,
  ExecutionLogSortDirection,
  ExecutionSubtreeResponseDto,
  ExecutionTreeNodeDto,
  getExecutionSubtree,
  listExecutionLogs
} from '../services/executionLogsApi';
import { GridRow, projectEntryRow, projectNodeRow } from './executionLogGrid';

const PAGE_SIZE = 50;
const POLL_INTERVAL_MS = 2000;
const GRID_COLUMN_COUNT = 6;

const formatRelativeTime = (timestampUtc: string): string => {
  const timestamp = new Date(timestampUtc);
  const deltaMs = Date.now() - timestamp.getTime();
  const minute = 60 * 1000;
  const hour = 60 * minute;
  const day = 24 * hour;

  if (Math.abs(deltaMs) < minute) return 'just now';
  if (Math.abs(deltaMs) < hour) return `${Math.round(deltaMs / minute)}m ago`;
  if (Math.abs(deltaMs) < day) return `${Math.round(deltaMs / hour)}h ago`;
  return `${Math.round(deltaMs / day)}d ago`;
};

const formatExactTime = (timestampUtc: string): string => new Date(timestampUtc).toLocaleString();

const GridRowView: React.FC<{ row: GridRow; expanded: boolean; onToggle: () => void }> = ({
  row,
  expanded,
  onToggle
}) => (
  <tr className="execution-logs-row" data-depth={row.depth} data-status={row.status}>
    <td className="execution-logs-expand-cell">
      {row.expandable && (
        <button
          type="button"
          className="execution-logs-expand"
          aria-expanded={expanded}
          aria-label={expanded ? 'Collapse sub-elements' : 'Expand sub-elements'}
          onClick={onToggle}
        >
          {expanded ? '▾' : '▸'}
        </button>
      )}
    </td>
    <td className="execution-logs-cell-timestamp">{row.timestamp}</td>
    <td className="execution-logs-cell-name" style={{ paddingLeft: `${0.5 + row.depth * 1.25}rem` }}>
      {row.name}
    </td>
    <td className="execution-logs-cell-type">{row.type}</td>
    <td className="execution-logs-cell-status">{row.status}</td>
    <td className="execution-logs-cell-info">{row.info}</td>
  </tr>
);

export const ExecutionLogsPage: React.FC = () => {
  const [items, setItems] = useState<ExecutionLogEntryDto[]>([]);
  const [loadingList, setLoadingList] = useState(true);
  const [error, setError] = useState<string | undefined>(undefined);
  const [nextPageToken, setNextPageToken] = useState<string | undefined>(undefined);

  const [sortBy, setSortBy] = useState<ExecutionLogListSortBy>('timestamp');
  const [sortDirection, setSortDirection] = useState<ExecutionLogSortDirection>('desc');
  const [filterTimestamp, setFilterTimestamp] = useState('');
  const [filterObjectName, setFilterObjectName] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [timestampMode, setTimestampMode] = useState<'exact' | 'relative'>('exact');

  const [expandedKeys, setExpandedKeys] = useState<Set<string>>(() => new Set());
  const [subtrees, setSubtrees] = useState<Record<string, ExecutionSubtreeResponseDto>>({});
  const [loadingSubtreeId, setLoadingSubtreeId] = useState<string | undefined>(undefined);
  const [subtreeError, setSubtreeError] = useState<string | undefined>(undefined);

  const listRequestId = useRef(0);

  const queryState = useMemo(
    () => ({ sortBy, sortDirection, filterTimestamp, filterObjectName, filterStatus }),
    [sortBy, sortDirection, filterTimestamp, filterObjectName, filterStatus]
  );

  const loadSubtree = async (id: string) => {
    setLoadingSubtreeId(id);
    setSubtreeError(undefined);
    try {
      const response = await getExecutionSubtree(id);
      setSubtrees((prev) => ({ ...prev, [id]: response }));
    } catch (err: any) {
      setSubtreeError(err?.message ?? 'Failed to load sub-elements');
    } finally {
      setLoadingSubtreeId((current) => (current === id ? undefined : current));
    }
  };

  // Each row toggles independently: flipping one key never touches the others.
  const toggleKey = (key: string) => {
    setExpandedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  };

  // Top-level rows fetch their full subtree once on first expand; nested toggles
  // reuse the cached tree with no further network calls.
  const toggleTopLevel = (id: string) => {
    if (!expandedKeys.has(id) && !subtrees[id]) {
      void loadSubtree(id);
    }
    toggleKey(id);
  };

  const fetchList = useCallback(async (silent = false) => {
    const requestId = ++listRequestId.current;
    if (!silent) setLoadingList(true);
    setError(undefined);
    try {
      const response = await listExecutionLogs({
        sortBy: queryState.sortBy,
        sortDirection: queryState.sortDirection,
        filterTimestamp: queryState.filterTimestamp.trim() || undefined,
        filterObjectName: queryState.filterObjectName.trim() || undefined,
        filterStatus: queryState.filterStatus.trim() || undefined,
        pageSize: PAGE_SIZE
      });

      if (requestId !== listRequestId.current) return;

      setItems(response.items ?? []);
      setNextPageToken(response.nextPageToken);
    } catch (err: any) {
      if (requestId !== listRequestId.current) return;
      if (!silent) {
        setItems([]);
        setNextPageToken(undefined);
      }
      setError(err?.message ?? 'Failed to load execution logs');
    } finally {
      if (requestId === listRequestId.current && !silent) {
        setLoadingList(false);
      }
    }
  }, [queryState]);

  useEffect(() => {
    void fetchList();
  }, [fetchList]);

  // Live updates: while any visible execution is still running, poll the list and
  // refresh any expanded running subtree so nested rows update without a reload.
  const hasRunning = useMemo(() => items.some((item) => item.finalStatus === 'running'), [items]);

  useEffect(() => {
    if (!hasRunning) return undefined;
    const interval = setInterval(() => {
      void fetchList(true);
      items.forEach((item) => {
        if (item.finalStatus === 'running' && expandedKeys.has(item.id)) {
          void loadSubtree(item.id);
        }
      });
    }, POLL_INTERVAL_MS);
    return () => clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasRunning, fetchList, expandedKeys, items]);

  const toggleSort = (column: ExecutionLogListSortBy) => {
    if (column === sortBy) {
      setSortDirection((previous) => (previous === 'asc' ? 'desc' : 'asc'));
      return;
    }
    setSortBy(column);
    setSortDirection(column === 'timestamp' ? 'desc' : 'asc');
  };

  const renderTimestamp = (timestampUtc: string) =>
    timestampMode === 'exact' ? formatExactTime(timestampUtc) : formatRelativeTime(timestampUtc);

  // Renders a sub-element node row and (when expanded) its descendants. The full
  // tree is already cached, so nested expansion is pure client-side state.
  const renderNodeRows = (node: ExecutionTreeNodeDto, parentKey: string, depth: number): React.ReactNode[] => {
    const row = projectNodeRow(node, parentKey, depth);
    const expanded = expandedKeys.has(row.key);
    const rows: React.ReactNode[] = [
      <GridRowView key={row.key} row={row} expanded={expanded} onToggle={() => toggleKey(row.key)} />
    ];
    if (row.expandable && expanded) {
      node.children.forEach((child) => {
        rows.push(...renderNodeRows(child, row.key, depth + 1));
      });
    }
    return rows;
  };

  const renderSubtreeRows = (id: string): React.ReactNode[] => {
    if (loadingSubtreeId === id && !subtrees[id]) {
      return [
        <tr key={`${id}-loading`} className="execution-logs-subtree-row">
          <td colSpan={GRID_COLUMN_COUNT}><span className="form-hint">Loading sub-elements...</span></td>
        </tr>
      ];
    }
    if (subtreeError && expandedKeys.has(id) && !subtrees[id]) {
      return [
        <tr key={`${id}-error`} className="execution-logs-subtree-row">
          <td colSpan={GRID_COLUMN_COUNT}><span className="form-error" role="alert">{subtreeError}</span></td>
        </tr>
      ];
    }
    const subtree = subtrees[id];
    if (!subtree) {
      return [];
    }
    if (subtree.root.children.length === 0) {
      return [
        <tr key={`${id}-empty`} className="execution-logs-subtree-row">
          <td colSpan={GRID_COLUMN_COUNT}><span className="form-hint">No sub-elements were recorded.</span></td>
        </tr>
      ];
    }
    const rows: React.ReactNode[] = [];
    subtree.root.children.forEach((child) => {
      rows.push(...renderNodeRows(child, id, 1));
    });
    return rows;
  };

  const renderGrid = () => (
    <section className="execution-logs-list" aria-label="Execution logs list">
      <div className="execution-logs-controls">
        <div className="execution-logs-filter-group">
          <label htmlFor="filter-timestamp">Timestamp</label>
          <input id="filter-timestamp" value={filterTimestamp} onChange={(e) => setFilterTimestamp(e.target.value)} placeholder="Filter timestamp" />
        </div>
        <div className="execution-logs-filter-group">
          <label htmlFor="filter-object">Object name</label>
          <input id="filter-object" value={filterObjectName} onChange={(e) => setFilterObjectName(e.target.value)} placeholder="Filter object" />
        </div>
        <div className="execution-logs-filter-group">
          <label htmlFor="filter-status">Status</label>
          <input id="filter-status" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} placeholder="Filter status" />
        </div>
        <div className="execution-logs-filter-group">
          <label htmlFor="timestamp-mode">Timestamp display</label>
          <select id="timestamp-mode" value={timestampMode} onChange={(e) => setTimestampMode(e.target.value as 'exact' | 'relative')}>
            <option value="exact">Exact local time</option>
            <option value="relative">Relative time</option>
          </select>
        </div>
      </div>

      {error && <div className="form-error" role="alert">{error}</div>}
      {loadingList && <div className="form-hint">Loading execution logs...</div>}
      {!loadingList && !error && items.length === 0 && <div className="form-hint">No execution logs found for the current filters.</div>}

      {!loadingList && !error && items.length > 0 && (
        <div className="execution-logs-scroll">
          <table className="execution-logs-table" role="table" aria-label="Execution logs">
            <thead>
              <tr>
                <th className="execution-logs-expand-cell" aria-label="Expand" />
                <th>
                  <button type="button" className="execution-logs-sort" onClick={() => toggleSort('timestamp')}>
                    Timestamp
                  </button>
                </th>
                <th>
                  <button type="button" className="execution-logs-sort" onClick={() => toggleSort('objectName')}>
                    Name
                  </button>
                </th>
                <th>Type</th>
                <th>
                  <button type="button" className="execution-logs-sort" onClick={() => toggleSort('status')}>
                    Status
                  </button>
                </th>
                <th>Additional information</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const row = projectEntryRow(item, renderTimestamp(item.timestampUtc));
                const expanded = expandedKeys.has(item.id);
                return (
                  <React.Fragment key={item.id}>
                    <GridRowView row={row} expanded={expanded} onToggle={() => toggleTopLevel(item.id)} />
                    {expanded && renderSubtreeRows(item.id)}
                  </React.Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {!loadingList && nextPageToken && <div className="form-hint">More logs are available.</div>}
    </section>
  );

  return (
    <div className="execution-logs-view">
      <h1>Execution Logs</h1>
      {renderGrid()}
    </div>
  );
};
