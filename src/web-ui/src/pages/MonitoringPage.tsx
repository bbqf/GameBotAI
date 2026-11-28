import React, { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../lib/api';
import { isFirstEntry } from '../lib/phoneHistory';
import { ConnectionState } from '../hooks/useConnectionState';
import { LayoutMode, useLayoutMode } from '../hooks/useLayoutMode';
import { usePolling } from '../hooks/usePolling';
import {
  MonitoredSessionDto,
  RestartStep,
  SessionRestartRecordDto,
  getRestart,
  listMonitoredSessions,
  startRestart
} from '../services/monitoringApi';
import { ScreenshotView } from '../components/monitoring/ScreenshotView';
import { SessionList, SessionStateChip } from '../components/monitoring/SessionList';
import { NotLiveBadge } from '../components/queues/NotLiveBadge';
import { deviceKind, sessionChip, startedText } from '../components/monitoring/sessionView';
import { Button, ConfirmDialog, FooterBar, PageHeader } from '../components/ui';
import type { ListState } from '../components/ui';
import './Monitoring.phone.css';

export const MONITORING_AREA_PATH = '/monitoring';
export const MONITORING_LIST_POLL_MS = 5000;
export const MONITORING_RESTART_POLL_MS = 2000;

export type MonitoringPageProps = {
  /** The layout. When missing, the page reads it with `useLayoutMode`. */
  layout?: LayoutMode;
  connectionState?: ConnectionState;
  /** Change this number to reload the list (for example after the connection returns). */
  reloadSignal?: number;
};

const stepText: Record<RestartStep, string> = {
  starting: 'starting',
  rebooting: 'stopping and starting the emulator',
  waiting_for_device: 'waiting for the emulator',
  reconnecting: 'connecting the session again',
  checking_capture: 'checking the picture',
  done: 'done'
};

const reasonText: Record<string, string> = {
  timeout: 'The emulator did not answer in time.',
  instance_not_found: 'The emulator instance was not found.',
  reboot_failed: 'The restart command failed.',
  reconnect_failed: 'The session could not connect again.',
  control_unavailable: 'The system cannot control the emulator.',
  cancelled: 'The restart was cancelled.',
  internal_error: 'An internal error occurred.'
};

const readSessionParam = (): string | null => new URLSearchParams(window.location.search).get('session');

const withSessionParam = (id: string | null): string => {
  const params = new URLSearchParams(window.location.search);
  if (id) params.set('session', id);
  else params.delete('session');
  return `${window.location.pathname}?${params.toString()}`;
};

const errorText = (err: unknown): string =>
  err instanceof ApiError || err instanceof Error ? err.message : 'The request failed.';

type RestartView = { sessionId: string; record: SessionRestartRecordDto };

/**
 * The Monitoring area (feature 141): the list of all sessions with a state, a live picture of the selected
 * session, and the restart of the whole emulator. One component serves the desktop and the phone layout. The
 * selected session is page state and the address query `session`, so a layout change keeps it (contract M-40).
 */
export const MonitoringPage: React.FC<MonitoringPageProps> = ({ layout, connectionState = 'online', reloadSignal = 0 }) => {
  const detectedLayout = useLayoutMode();
  const phone = (layout ?? detectedLayout) === 'phone';
  const lost = connectionState === 'lost';

  const [sessions, setSessions] = useState<MonitoredSessionDto[] | null>(null);
  const [listError, setListError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(readSessionParam);
  const [ended, setEnded] = useState(false);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [restart, setRestart] = useState<RestartView | null>(null);
  const [restartMessage, setRestartMessage] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const selectedRef = useRef<string | null>(selectedId);
  selectedRef.current = selectedId;

  const loadList = useCallback(async () => {
    try {
      const list = await listMonitoredSessions();
      setSessions(list);
      setListError(null);
    } catch (err) {
      setListError(errorText(err));
      setSessions((prev) => prev ?? []);
    }
  }, []);

  usePolling(() => { void loadList(); }, { intervalMs: MONITORING_LIST_POLL_MS, enabled: !lost });

  const firstReload = useRef(true);
  useEffect(() => {
    if (firstReload.current) {
      firstReload.current = false;
      return;
    }
    void loadList();
  }, [reloadSignal, loadList]);

  // When the connection is lost at the start, load the list once so the screen is not empty.
  useEffect(() => {
    if (lost && sessions === null) void loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lost]);

  // The browser back and forward controls change the address query. The page follows it.
  useEffect(() => {
    const onPopState = () => {
      setSelectedId(readSessionParam());
      setEnded(false);
    };
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  const select = (id: string | null) => {
    setEnded(false);
    setSelectedId(id);
    if (phone && id) {
      const idx = typeof window.history.state?.idx === 'number' ? window.history.state.idx : 0;
      window.history.pushState({ ...(window.history.state ?? {}), idx: idx + 1 }, '', withSessionParam(id));
    } else {
      window.history.replaceState(window.history.state, '', withSessionParam(id));
    }
  };

  // The selected session left the list: show "Session ended" and clear the selection (contract M-13).
  useEffect(() => {
    if (sessions === null || selectedId === null) return;
    if (!sessions.some((s) => s.id === selectedId)) {
      setEnded(true);
      setSelectedId(null);
      window.history.replaceState(window.history.state, '', withSessionParam(null));
    }
  }, [sessions, selectedId]);

  // A new selection clears the restart data of the old session.
  useEffect(() => {
    setRestartMessage(null);
    setDialogOpen(false);
  }, [selectedId]);

  const current = sessions?.find((s) => s.id === selectedId) ?? null;
  const record = restart && restart.sessionId === selectedId ? restart.record : null;

  const pollRestart = useCallback(async () => {
    const id = selectedRef.current;
    if (!id) return;
    try {
      const next = await getRestart(id);
      if (selectedRef.current === id) setRestart({ sessionId: id, record: next });
    } catch {
      // A failed read changes nothing. The page never sends a new restart by itself (contract M-33).
    }
  }, []);

  // When a restart ends, the list refreshes at once, so the state chip and the restart control follow.
  const recordState = record?.state;
  useEffect(() => {
    if (recordState === 'success' || recordState === 'failure') void loadList();
  }, [recordState, loadList]);

  const shouldPollRestart = !!current && !lost && (record?.state === 'restarting' || (!record && current.state === 'restarting'));
  usePolling(() => { void pollRestart(); }, { intervalMs: MONITORING_RESTART_POLL_MS, enabled: shouldPollRestart });

  const confirmRestart = async () => {
    if (!current || starting) return;
    const id = current.id;
    setDialogOpen(false);
    setStarting(true);
    setRestartMessage(null);
    try {
      const started = await startRestart(id);
      setRestart({ sessionId: id, record: started });
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setRestartMessage('A restart is already running for this emulator.');
        void pollRestart();
      } else {
        setRestartMessage(`Restart failed. ${errorText(err)}`);
      }
    } finally {
      setStarting(false);
    }
  };

  const restarting = current?.state === 'restarting' || record?.state === 'restarting';
  const restartDisabled = !current || !current.restartAvailable || restarting || starting || lost;

  const renderRestartStatus = () => {
    if (record?.state === 'restarting') {
      return <p role="status" data-testid="restart-status">Restarting: {stepText[record.step] ?? record.step}</p>;
    }
    if (current?.state === 'restarting' && !record) {
      return <p role="status" data-testid="restart-status">Restarting</p>;
    }
    if (record?.state === 'success') {
      return <p role="status" data-testid="restart-status">Emulator restarted</p>;
    }
    if (record?.state === 'failure') {
      const reason = record.reason ? (reasonText[record.reason] ?? record.reason) : '';
      return <p className="error" role="alert" data-testid="restart-status">Restart failed. {reason}</p>;
    }
    return null;
  };

  const renderDetail = (session: MonitoredSessionDto) => {
    const restartButton = (extraClass?: string) => (
      <Button
        variant="secondary"
        icon="reload"
        className={extraClass}
        disabled={restartDisabled}
        onClick={() => setDialogOpen(true)}
      >
        Restart emulator
      </Button>
    );
    const kind = deviceKind(session.ownerQueueDeviceLiveness);
    return (
      <section className="monitoring-detail" data-testid="monitoring-detail">
        <PageHeader
          title={session.name}
          onBack={phone ? closeDetail : () => select(null)}
          status={sessionChip(session.state)}
          actions={phone ? undefined : restartButton()}
        />
        <div className="ui-detail-grid">
          <ScreenshotView sessionId={session.id} sessionName={session.name} enabled={!lost} />
          <div className="ui-detail-side">
            <dl className="ui-kv">
              <div className="ui-kv__row">
                <dt className="ui-kv__label">State</dt>
                <dd className="ui-kv__value"><SessionStateChip state={session.state} /></dd>
              </div>
              <div className="ui-kv__row">
                <dt className="ui-kv__label">Queue</dt>
                <dd className="ui-kv__value">{session.ownerQueueName ?? '—'}</dd>
              </div>
              <div className="ui-kv__row">
                <dt className="ui-kv__label">Device</dt>
                <dd className="ui-kv__value">{kind === 'not_live' ? <NotLiveBadge /> : kind === 'live' ? 'Live' : '—'}</dd>
              </div>
              <div className="ui-kv__row">
                <dt className="ui-kv__label">Sequence</dt>
                {session.currentSequenceName ? (
                  <dd className="ui-kv__value" data-testid="monitoring-sequence">{session.currentSequenceName}</dd>
                ) : (
                  <dd className="ui-kv__value">—</dd>
                )}
              </div>
              {session.state === 'no_answer' && session.reason && (
                <div className="ui-kv__row">
                  <dt className="ui-kv__label">Reason</dt>
                  <dd className="ui-kv__value">{session.reason}</dd>
                </div>
              )}
              <div className="ui-kv__row">
                <dt className="ui-kv__label">Started</dt>
                <dd className="ui-kv__value">{startedText(session.startedAtUtc)}</dd>
              </div>
            </dl>
            {renderRestartStatus()}
            {restartMessage && <p className="form-hint" role="alert" data-testid="restart-message">{restartMessage}</p>}
            {!session.restartAvailable && (
              <p className="form-hint" data-testid="restart-unavailable">
                Restart is not available for this session.
                {session.restartUnavailableReason ? ` ${session.restartUnavailableReason}` : ''}
              </p>
            )}
          </div>
        </div>
        {phone && <FooterBar right={restartButton('ui-button--block')} />}
        <ConfirmDialog
          open={dialogOpen}
          title={`Restart emulator ${session.name}?`}
          message="The whole emulator stops and starts again."
          confirmText="Restart"
          danger
          details={session.ownerQueueId ? (
            <p role="note" data-testid="restart-queue-warning">
              Queue {session.ownerQueueName ?? '(no name)'} uses this emulator. The queue work will stop for a short time.
            </p>
          ) : undefined}
          onCancel={() => setDialogOpen(false)}
          onConfirm={() => { void confirmRestart(); }}
        />
      </section>
    );
  };

  /** On-screen back (phone): back in the history, or the list when this is the first entry. */
  function closeDetail() {
    if (!isFirstEntry()) {
      window.history.back();
      return;
    }
    select(null);
  }

  let listState: ListState = 'ready';
  if (sessions === null) listState = { kind: 'loading' };
  else if (sessions.length === 0 && listError) listState = { kind: 'error', text: listError, onRetry: () => void loadList() };
  else if (sessions.length === 0) listState = { kind: 'empty', text: 'No running sessions.' };

  const notices = (
    <>
      {listError && <p className="form-hint" role="alert">{listError}</p>}
      {ended && <p role="status" data-testid="session-ended">Session ended</p>}
    </>
  );

  return (
    <div
      className={`monitoring-page monitoring-page--${phone ? 'phone' : 'desktop'}`}
      data-testid="monitoring-page"
    >
      {current ? renderDetail(current) : (
        <>
          <PageHeader title="Monitor" />
          {notices}
          <SessionList sessions={sessions ?? []} state={listState} onSelect={select} mode={phone ? 'phone' : 'desktop'} />
        </>
      )}
    </div>
  );
};
