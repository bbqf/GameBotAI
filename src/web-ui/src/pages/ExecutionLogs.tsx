import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ExecutionLogDetailDto,
  ExecutionLogEntryDto,
  ExecutionLogListSortBy,
  ExecutionLogSortDirection,
  ExecutionLogStepDeepLinkDto,
  ExecutionSubtreeResponseDto,
  ExecutionTreeNodeDto,
  getExecutionLogDetail,
  getExecutionSubtree,
  listExecutionLogs
} from '../services/executionLogsApi';
import { useNavigationCollapse } from '../hooks/useNavigationCollapse';

const PAGE_SIZE = 50;
const POLL_INTERVAL_MS = 2000;

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

const formatExactTime = (timestampUtc: string): string =>
  new Date(timestampUtc).toLocaleString();

const formatExitCondition = (exitCondition?: string): string => {
  switch (exitCondition) {
    case 'image_detected':
      return 'Image detected';
    case 'timeout_elapsed':
      return 'Timeout elapsed';
    case 'image_unavailable':
      return 'Image unavailable';
    default:
      return exitCondition ?? 'Unknown';
  }
};

const openAuthoringDeepLink = (deepLink?: ExecutionLogStepDeepLinkDto): void => {
  if (!deepLink) {
    return;
  }

  const params = new URLSearchParams(window.location.search);
  params.set('area', 'authoring');
  params.set('tab', 'Sequences');
  params.set('id', deepLink.sequenceId);
  if (deepLink.resolutionStatus === 'resolved' && deepLink.stepId) {
    params.set('stepId', deepLink.stepId);
    params.delete('missingStep');
  } else {
    params.delete('stepId');
    params.set('missingStep', '1');
  }

  window.location.assign(`${window.location.pathname}?${params.toString()}`);
};

const ExecutionTreeNode: React.FC<{ node: ExecutionTreeNodeDto; depth: number }> = ({ node, depth }) => (
  <li className="execution-tree-node" data-node-kind={node.nodeKind} data-status={node.status}>
    <div className="execution-tree-node-row" style={{ paddingLeft: `${depth * 1.25}rem` }}>
      <span className="execution-tree-node-kind">{node.nodeKind}</span>
      <strong className="execution-tree-node-label">{node.label}</strong>
      <span className="execution-tree-node-status">{node.status}</span>
      {node.message && <span className="execution-tree-node-message"> — {node.message}</span>}
      {typeof node.appliedDelayMs === 'number' && (
        <span className="form-hint"> (delay {node.appliedDelayMs} ms)</span>
      )}
      {node.deepLink && (
        <button
          type="button"
          className="execution-tree-deeplink"
          onClick={() => openAuthoringDeepLink(node.deepLink)}
        >
          Open in sequence
        </button>
      )}
    </div>
    {node.conditionTrace && (
      <div className="form-hint" style={{ paddingLeft: `${depth * 1.25}rem` }}>
        Condition: final result {node.conditionTrace.finalResult ? 'true' : 'false'} ({node.conditionTrace.selectedBranch} branch)
      </div>
    )}
    {node.detailAttributes && (
      <div className="form-hint" style={{ paddingLeft: `${depth * 1.25}rem` }}>
        Wait: timeout {typeof node.detailAttributes.timeoutMs === 'number' ? `${node.detailAttributes.timeoutMs} ms` : 'n/a'};
        exit {formatExitCondition(node.detailAttributes.exitCondition)}.
      </div>
    )}
    {node.children.length > 0 && (
      <ul className="execution-tree-children">
        {node.children.map((child, index) => (
          <ExecutionTreeNode key={`${child.nodeKind}-${child.order}-${index}`} node={child} depth={depth + 1} />
        ))}
      </ul>
    )}
  </li>
);

export const ExecutionLogsPage: React.FC = () => {
  const [items, setItems] = useState<ExecutionLogEntryDto[]>([]);
  const [detail, setDetail] = useState<ExecutionLogDetailDto | undefined>(undefined);
  const [selectedId, setSelectedId] = useState<string | undefined>(undefined);
  const [loadingList, setLoadingList] = useState(true);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [error, setError] = useState<string | undefined>(undefined);
  const [detailError, setDetailError] = useState<string | undefined>(undefined);
  const [nextPageToken, setNextPageToken] = useState<string | undefined>(undefined);

  const [sortBy, setSortBy] = useState<ExecutionLogListSortBy>('timestamp');
  const [sortDirection, setSortDirection] = useState<ExecutionLogSortDirection>('desc');
  const [filterTimestamp, setFilterTimestamp] = useState('');
  const [filterObjectName, setFilterObjectName] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [timestampMode, setTimestampMode] = useState<'exact' | 'relative'>('exact');
  const [showPhoneDetail, setShowPhoneDetail] = useState(false);

  const [expandedId, setExpandedId] = useState<string | undefined>(undefined);
  const [subtrees, setSubtrees] = useState<Record<string, ExecutionSubtreeResponseDto>>({});
  const [loadingSubtreeId, setLoadingSubtreeId] = useState<string | undefined>(undefined);
  const [subtreeError, setSubtreeError] = useState<string | undefined>(undefined);

  const { isCollapsed: isPhone } = useNavigationCollapse(640);
  const listRequestId = useRef(0);
  const detailRequestId = useRef(0);

  const queryState = useMemo(() => ({
    sortBy,
    sortDirection,
    filterTimestamp,
    filterObjectName,
    filterStatus
  }), [sortBy, sortDirection, filterTimestamp, filterObjectName, filterStatus]);

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

  const toggleExpand = (id: string) => {
    setExpandedId((current) => {
      if (current === id) {
        return undefined;
      }
      if (!subtrees[id]) {
        void loadSubtree(id);
      }
      return id;
    });
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

      if (response.items.length === 0) {
        setSelectedId(undefined);
        setDetail(undefined);
        setShowPhoneDetail(false);
      } else {
        setSelectedId((previous) => {
          if (previous && response.items.some((item) => item.id === previous)) {
            return previous;
          }
          return response.items[0].id;
        });
      }
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

  // Live updates: while any visible execution is still running, poll the list (and any
  // expanded in-progress subtree) so sub-elements update without a manual reload.
  const hasRunning = useMemo(() => items.some((item) => item.finalStatus === 'running'), [items]);

  useEffect(() => {
    if (!hasRunning) return undefined;
    const interval = setInterval(() => {
      void fetchList(true);
      if (expandedId) {
        void loadSubtree(expandedId);
      }
    }, POLL_INTERVAL_MS);
    return () => clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasRunning, fetchList, expandedId]);

  useEffect(() => {
    if (!selectedId) return;

    let isMounted = true;
    const requestId = ++detailRequestId.current;

    const load = async () => {
      setLoadingDetail(true);
      setDetailError(undefined);
      try {
        const response = await getExecutionLogDetail(selectedId);
        if (!isMounted || requestId !== detailRequestId.current) return;
        setDetail(response);
      } catch (err: any) {
        if (!isMounted || requestId !== detailRequestId.current) return;
        setDetail(undefined);
        setDetailError(err?.message ?? 'Failed to load execution detail');
      } finally {
        if (isMounted && requestId === detailRequestId.current) {
          setLoadingDetail(false);
        }
      }
    };

    void load();

    return () => {
      isMounted = false;
    };
  }, [selectedId]);

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

  const handleSelectRow = (id: string) => {
    setSelectedId(id);
    if (isPhone) {
      setShowPhoneDetail(true);
    }
  };

  const openStepDeepLink = (step: ExecutionLogDetailDto['stepOutcomes'][number]) => {
    openAuthoringDeepLink(step.deepLink);
  };

  const isExpandable = (item: ExecutionLogEntryDto) =>
    item.executionType === 'sequence' || item.childCount > 0;

  const renderSubtree = (id: string) => {
    if (loadingSubtreeId === id && !subtrees[id]) {
      return <div className="form-hint">Loading sub-elements...</div>;
    }
    if (subtreeError && expandedId === id && !subtrees[id]) {
      return <div className="form-error" role="alert">{subtreeError}</div>;
    }
    const subtree = subtrees[id];
    if (!subtree) {
      return null;
    }
    if (subtree.root.children.length === 0) {
      return <div className="form-hint">No sub-elements were recorded.</div>;
    }
    return (
      <ul className="execution-tree" aria-label="Execution sub-elements">
        {subtree.root.children.map((child, index) => (
          <ExecutionTreeNode key={`${child.nodeKind}-${child.order}-${index}`} node={child} depth={0} />
        ))}
      </ul>
    );
  };

  const renderList = () => (
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
        <table className="execution-logs-table" role="table" aria-label="Execution logs">
          <thead>
            <tr>
              <th>
                <button type="button" className="execution-logs-sort" onClick={() => toggleSort('timestamp')}>
                  Timestamp
                </button>
              </th>
              <th>
                <button type="button" className="execution-logs-sort" onClick={() => toggleSort('objectName')}>
                  Object Name
                </button>
              </th>
              <th>
                <button type="button" className="execution-logs-sort" onClick={() => toggleSort('status')}>
                  Status
                </button>
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => {
              const isSelected = item.id === selectedId;
              const expandable = isExpandable(item);
              const expanded = expandedId === item.id;
              return (
                <React.Fragment key={item.id}>
                  <tr
                    className={isSelected ? 'execution-logs-row execution-logs-row--selected' : 'execution-logs-row'}
                    onClick={() => handleSelectRow(item.id)}
                  >
                    <td>{renderTimestamp(item.timestampUtc)}</td>
                    <td>
                      {expandable && (
                        <button
                          type="button"
                          className="execution-logs-expand"
                          aria-expanded={expanded}
                          aria-label={expanded ? 'Collapse sub-elements' : 'Expand sub-elements'}
                          onClick={(e) => { e.stopPropagation(); toggleExpand(item.id); }}
                        >
                          {expanded ? '▾' : '▸'}
                        </button>
                      )}
                      {item.objectRef.displayNameSnapshot}
                    </td>
                    <td>{item.finalStatus}</td>
                  </tr>
                  {expanded && (
                    <tr className="execution-logs-subtree-row">
                      <td colSpan={3}>{renderSubtree(item.id)}</td>
                    </tr>
                  )}
                </React.Fragment>
              );
            })}
          </tbody>
        </table>
      )}

      {!loadingList && nextPageToken && <div className="form-hint">More logs are available.</div>}
    </section>
  );

  const renderDetail = () => (
    <section className="execution-logs-detail" aria-label="Execution log detail">
      {isPhone && (
        <div className="actions">
          <button type="button" onClick={() => setShowPhoneDetail(false)}>Back to list</button>
        </div>
      )}

      {!selectedId && <div className="form-hint">Select an execution to view details.</div>}
      {detailError && <div className="form-error" role="alert">{detailError}</div>}
      {loadingDetail && <div className="form-hint">Loading details...</div>}

      {!loadingDetail && detail && (
        <>
          <h2>Execution details</h2>
          <p>{detail.summary}</p>

          <div className="execution-logs-detail-section">
            <h3>Related objects</h3>
            {detail.relatedObjects.length === 0 && <div className="form-hint">No related objects available.</div>}
            {detail.relatedObjects.length > 0 && (
              <ul>
                {detail.relatedObjects.map((obj) => (
                  <li key={`${obj.targetType}-${obj.targetId}`}>
                    {obj.label} ({obj.targetType})
                    {!obj.isAvailable && obj.unavailableReason && <span> — {obj.unavailableReason}</span>}
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="execution-logs-detail-section">
            <h3>Snapshot</h3>
            {!detail.snapshot.isAvailable && <div className="form-hint">No snapshot was captured for this execution.</div>}
            {detail.snapshot.isAvailable && (
              <div className="form-hint">{detail.snapshot.caption ?? 'Snapshot is available for this execution.'}</div>
            )}
          </div>

          <div className="execution-logs-detail-section">
            <h3>Step outcomes</h3>
            {detail.stepOutcomes.length === 0 && <div className="form-hint">No step outcomes were recorded.</div>}
            {detail.stepOutcomes.length > 0 && (
              <ul>
                {detail.stepOutcomes.map((step, index) => (
                  <li key={`${step.stepName}-${index}`}>
                    <strong>{step.stepName}</strong>{step.commandName ? ` (${step.commandName})` : ''}: {step.status} — {step.message}
                    <div className="form-hint">
                      Applied delay: {typeof step.appliedDelayMs === 'number' ? `${step.appliedDelayMs} ms` : 'n/a'}
                    </div>
                    {step.deepLink && (
                      <div className="actions" style={{ marginTop: '0.25rem' }}>
                        <button type="button" onClick={() => openStepDeepLink(step)}>
                          Open in sequence
                        </button>
                        {step.deepLink.resolutionStatus !== 'resolved' && (
                          <span className="form-hint">Referenced step missing. Opening sequence overview.</span>
                        )}
                      </div>
                    )}
                    {step.conditionTrace && (
                      <div className="form-hint">
                        Condition trace: final result {step.conditionTrace.finalResult ? 'true' : 'false'} ({step.conditionTrace.selectedBranch} branch)
                      </div>
                    )}
                    {step.detailAttributes && (
                      <div className="form-hint">
                        <div>
                          Wait settings: timeout {typeof step.detailAttributes.timeoutMs === 'number' ? `${step.detailAttributes.timeoutMs} ms` : 'n/a'},
                          effective timeout {typeof step.detailAttributes.effectiveTimeoutMs === 'number' ? `${step.detailAttributes.effectiveTimeoutMs} ms` : 'n/a'}.
                        </div>
                        <div>
                          Image: {step.detailAttributes.referenceImageId ?? 'not configured'};
                          confidence {typeof step.detailAttributes.confidence === 'number' ? step.detailAttributes.confidence.toFixed(2) : 'default'};
                          load status {step.detailAttributes.imageLoadStatus ?? 'n/a'}.
                        </div>
                        <div>
                          Exit condition: {formatExitCondition(step.detailAttributes.exitCondition)}.
                        </div>
                      </div>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </section>
  );

  return (
    <div className="execution-logs-view">
      <h1>Execution Logs</h1>
      {!isPhone && (
        <div className="execution-logs-layout execution-logs-layout--desktop">
          {renderList()}
          {renderDetail()}
        </div>
      )}
      {isPhone && (
        <div className="execution-logs-layout execution-logs-layout--phone">
          {!showPhoneDetail && renderList()}
          {showPhoneDetail && renderDetail()}
        </div>
      )}
    </div>
  );
};
