import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  ExecutionLogDetailDto,
  ExecutionLogEntryDto,
  ExecutionLogListSortBy,
  ExecutionLogSortDirection,
  getExecutionLogDetail,
  listExecutionLogs
} from '../services/executionLogsApi';
import { useNavigationCollapse } from '../hooks/useNavigationCollapse';

const PAGE_SIZE = 50;

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

  useEffect(() => {
    let isMounted = true;
    const requestId = ++listRequestId.current;

    const load = async () => {
      setLoadingList(true);
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

        if (!isMounted || requestId !== listRequestId.current) return;

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
        if (!isMounted || requestId !== listRequestId.current) return;
        setItems([]);
        setNextPageToken(undefined);
        setError(err?.message ?? 'Failed to load execution logs');
      } finally {
        if (isMounted && requestId === listRequestId.current) {
          setLoadingList(false);
        }
      }
    };

    void load();
    return () => {
      isMounted = false;
    };
  }, [queryState]);

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
    const deepLink = step.deepLink;
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
              return (
                <tr
                  key={item.id}
                  className={isSelected ? 'execution-logs-row execution-logs-row--selected' : 'execution-logs-row'}
                  onClick={() => handleSelectRow(item.id)}
                >
                  <td>{renderTimestamp(item.timestampUtc)}</td>
                  <td>{item.objectRef.displayNameSnapshot}</td>
                  <td>{item.finalStatus}</td>
                </tr>
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
                    <strong>{step.stepName}</strong>: {step.status} — {step.message}
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
