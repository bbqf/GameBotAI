import React, { useCallback, useEffect, useRef, useState } from 'react';
import { PhoneCard } from '../phone/PhoneCard';
import { ConnectionState } from '../../hooks/useConnectionState';
import { usePolling } from '../../hooks/usePolling';
import { toggleTemplateEntry } from '../../lib/queueEntryToggle';
import { isFirstEntry, replaceScreen } from '../../lib/phoneHistory';
import { ApiError } from '../../lib/api';
import {
  getQueue,
  getQueueMonitor,
  listQueues,
  runNextEntry,
  startQueue,
  stopQueue,
  QueueDetailDto,
  QueueDto,
  QueueMonitorDto
} from '../../services/queues';
import { getQueueTemplate, saveQueueTemplate, QueueTemplateDetail } from '../../services/queueTemplates';
import { buildQueueActivity, buildScheduleRows, describeNow } from './queueActivity';
import { IconButton } from './IconButton';
import { QueuePauseControls } from './QueuePauseControls';
import { TemplateChangesNotice } from './TemplateChangesNotice';
import { PhoneEntryPage } from './PhoneEntryPage';
import '../../features/execution/StatusChip.css';

export const PHONE_QUEUES_POLL_MS = 5000;

export type PhoneQueuesViewProps = {
  connectionState?: ConnectionState;
  /** Change this number to reload the data (for example after the connection returns). */
  reloadSignal?: number;
  /** The open queue. The owner of the history sets it. Null shows the list. */
  queueId?: string | null;
  /** Called with a queue id to open it, or with null to show the list. */
  onOpenQueue?: (id: string | null) => void;
  /** The open entry of the open queue (feature 185). Set, the view shows the entry page. */
  entryId?: string | null;
  /** Called with a queue id and an entry ID to open the entry page, or with a null entry ID to show the queue detail. */
  onOpenEntry?: (queueId: string, entryId: string | null) => void;
  /** The app raises this counter when the phone back step met the leave guard. The entry page then asks the operator. */
  leaveRequest?: number;
};

/** The text of a row of a template that needs an upgrade. Such an entry has no ID, so it cannot open (rule P-03). */
export const UPGRADE_HINT = 'Upgrade the template to open the entry settings.';

const errorText = (err: unknown): string =>
  err instanceof ApiError || err instanceof Error ? err.message : 'The request failed.';

const QueueStatusChip: React.FC<{ status: string }> = ({ status }) => {
  const running = status === 'Running';
  return (
    <span
      className={running ? 'status-chip status-chip--running' : 'status-chip status-chip--unknown'}
      data-testid="queue-status"
    >
      <span className="status-chip__dot" aria-hidden />
      <span>{status}</span>
    </span>
  );
};

const StaleMark: React.FC = () => (
  <span className="phone-stale" data-testid="stale-mark">Old data</span>
);

/** Phone view of the queues: cards with a summary, the queue activity, start and stop, and the entry enable switches (contract C-08). */
export const PhoneQueuesView: React.FC<PhoneQueuesViewProps> = ({
  connectionState = 'online',
  reloadSignal = 0,
  queueId = null,
  onOpenQueue,
  entryId = null,
  onOpenEntry,
  leaveRequest = 0
}) => {
  const [queues, setQueues] = useState<QueueDto[] | null>(null);
  const [monitors, setMonitors] = useState<Record<string, QueueMonitorDto>>({});
  const [listStale, setListStale] = useState(false);
  const [detail, setDetail] = useState<QueueDetailDto | null>(null);
  const [template, setTemplate] = useState<QueueTemplateDetail | null>(null);
  const [monitor, setMonitor] = useState<QueueMonitorDto | null>(null);
  const [detailStale, setDetailStale] = useState(false);
  // True after the first full read of the detail, the template, and the monitor. The entry page waits for it.
  const [detailLoaded, setDetailLoaded] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [busy, setBusy] = useState(false);
  const [templateSaves, setTemplateSaves] = useState(0);
  const [message, setMessage] = useState<string | null>(null);
  const selectedRef = useRef<string | null>(queueId);
  selectedRef.current = queueId;
  const hasListRef = useRef(false);
  const hasDetailRef = useRef<string | null>(null);

  const lost = connectionState === 'lost';

  const loadList = useCallback(async () => {
    try {
      const data = await listQueues();
      const list = Array.isArray(data) ? data : [];
      hasListRef.current = true;
      setQueues(list);
      setListStale(false);
      setMessage(null);
      const running = list.filter((q) => q.status === 'Running');
      const results = await Promise.all(
        running.map(async (q) => {
          try {
            return [q.id, await getQueueMonitor(q.id)] as const;
          } catch {
            return [q.id, null] as const;
          }
        })
      );
      const next: Record<string, QueueMonitorDto> = {};
      for (const [id, m] of results) if (m) next[id] = m;
      setMonitors(next);
    } catch (err) {
      if (hasListRef.current) {
        setListStale(true);
      } else {
        setMessage(errorText(err));
        setQueues([]);
      }
    }
  }, []);

  const loadDetail = useCallback(async () => {
    const id = selectedRef.current;
    if (!id) return;
    let failed = false;
    let d: QueueDetailDto;
    try {
      d = await getQueue(id);
    } catch (err) {
      if (selectedRef.current !== id) return;
      if (err instanceof ApiError && err.status === 404) {
        setNotFound(true);
        return;
      }
      if (hasDetailRef.current === id) setDetailStale(true);
      else setMessage(errorText(err));
      return;
    }
    if (selectedRef.current !== id) return;
    setNotFound(false);
    hasDetailRef.current = id;
    setDetail(d);
    let t: QueueTemplateDetail | null = null;
    if (d.linkedTemplateId) {
      try {
        t = await getQueueTemplate(d.linkedTemplateId);
      } catch (err) {
        failed = true;
        if (selectedRef.current === id) setMessage(errorText(err));
      }
    }
    let m: QueueMonitorDto | null = null;
    try {
      m = (await getQueueMonitor(id)) ?? null;
    } catch {
      failed = true;
    }
    if (selectedRef.current !== id) return;
    if (!failed || t) setTemplate(t);
    if (m) setMonitor(m);
    setDetailStale(failed);
    setDetailLoaded(true);
    if (!failed) setMessage(null);
  }, []);

  usePolling(() => { void loadList(); }, { intervalMs: PHONE_QUEUES_POLL_MS, enabled: !queueId && !lost });
  usePolling(() => { void loadDetail(); }, { intervalMs: PHONE_QUEUES_POLL_MS, enabled: !!queueId && !lost });

  // When the connection is lost at the start, load the data once so the screen is not empty.
  useEffect(() => {
    if (!lost) return;
    if (!queueId && queues === null) void loadList();
    if (queueId && !detail) void loadDetail();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lost, queueId]);

  const firstReload = useRef(true);
  useEffect(() => {
    if (firstReload.current) {
      firstReload.current = false;
      return;
    }
    if (selectedRef.current) void loadDetail();
    else void loadList();
  }, [reloadSignal, loadList, loadDetail]);

  // A new queue id clears the data of the old queue.
  useEffect(() => {
    hasDetailRef.current = null;
    setDetail(null);
    setTemplate(null);
    setMonitor(null);
    setDetailStale(false);
    setDetailLoaded(false);
    setNotFound(false);
    setMessage(null);
  }, [queueId]);

  // Back from the entry page: the list shows again at the place where the operator left it (quickstart, phone step 5).
  const scrollBeforeEntry = useRef<number | null>(null);
  const wasOnEntry = useRef(false);
  useEffect(() => {
    if (entryId) {
      wasOnEntry.current = true;
      return;
    }
    if (wasOnEntry.current && scrollBeforeEntry.current !== null) {
      const top = scrollBeforeEntry.current;
      window.requestAnimationFrame(() => window.scrollTo(0, top));
    }
    wasOnEntry.current = false;
  }, [entryId]);

  const openEntry = (id: string, entry: string) => {
    scrollBeforeEntry.current = window.scrollY;
    onOpenEntry?.(id, entry);
  };

  const open = (id: string) => onOpenQueue?.(id);

  /** On-screen back (contract H-04): back in the history, or the list when this is the first entry. */
  const close = () => {
    if (!isFirstEntry()) {
      window.history.back();
      return;
    }
    replaceScreen({ area: 'queues', queueId: null });
    onOpenQueue?.(null);
  };

  /** On-screen back of the entry page: back in the history, or the queue detail when this is the first entry. */
  const closeEntry = () => {
    if (!isFirstEntry()) {
      window.history.back();
      return;
    }
    if (queueId) {
      replaceScreen({ area: 'queues', queueId, entryId: null });
      onOpenEntry?.(queueId, null);
    }
  };

  const goToList = () => {
    replaceScreen({ area: 'queues', queueId: null });
    onOpenQueue?.(null);
  };

  const toggleRun = async (queue: Pick<QueueDto, 'id' | 'status'>) => {
    if (busy || lost) return;
    setBusy(true);
    setMessage(null);
    try {
      const updated = queue.status === 'Running' ? await stopQueue(queue.id) : await startQueue(queue.id);
      setQueues((prev) => (prev ?? []).map((q) => (q.id === queue.id ? { ...q, ...updated } : q)));
      setDetail((prev) => (prev && prev.id === queue.id ? { ...prev, ...updated } : prev));
    } catch (err) {
      setMessage(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  const toggleEntry = async (index: number) => {
    if (!template || busy || lost) return;
    setBusy(true);
    setMessage(null);
    try {
      const saved = await saveQueueTemplate(toggleTemplateEntry(template, index));
      setTemplate(saved);
      // A switch on a running queue changes the template only. The notice reads the waiting changes again.
      setTemplateSaves((count) => count + 1);
    } catch (err) {
      setMessage(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  /**
   * Run next (feature 184): one call, no retry (mobile principle 10). The detail is read again after the call, so the row
   * shows the new state. A refused call shows its text in the message area.
   */
  const runNext = async (queueIdToRun: string, entryId: string) => {
    if (busy || lost) return;
    setBusy(true);
    setMessage(null);
    let refused: string | null = null;
    try {
      await runNextEntry(queueIdToRun, entryId);
    } catch (err) {
      refused = errorText(err);
    }
    setBusy(false);
    // The read clears the message on success, so the text of a refused call is set after it.
    await loadDetail();
    if (refused) setMessage(refused);
  };

  const runLabel = (status: string) => (status === 'Running' ? 'Stop' : 'Start');

  if (queueId) {
    if (notFound) {
      return (
        <section className="phone-queue-detail" data-testid="phone-queue-detail">
          <p role="alert">Queue not found</p>
          <button type="button" onClick={goToList}>Go to the queue list</button>
        </section>
      );
    }
    const rows = detail ? buildScheduleRows(monitor, template, detail) : [];
    if (entryId) {
      // The entry page waits for the first full read, so a reload does not flash "Entry not found".
      if (!detailLoaded || !detail) {
        return (
          <section className="phone-queue-detail" data-testid="phone-queue-detail">
            <button type="button" className="phone-queue-detail__back" onClick={closeEntry}>← Back</button>
            {message ? <p className="form-hint" role="alert">{message}</p> : <p role="status">Loading…</p>}
          </section>
        );
      }
      return (
        <PhoneEntryPage
          detail={detail}
          template={template}
          entryId={entryId}
          connectionState={connectionState}
          leaveRequest={leaveRequest}
          onBack={closeEntry}
          onSaved={(saved) => {
            setTemplate(saved);
            // A save on a running queue changes the template only. The notice reads the waiting changes again.
            setTemplateSaves((count) => count + 1);
          }}
        />
      );
    }
    return (
      <section className="phone-queue-detail" data-testid="phone-queue-detail">
        <button type="button" className="phone-queue-detail__back" onClick={close}>← Back</button>
        {!detail && !message && <p role="status">Loading…</p>}
        {message && <p className="form-hint" role="alert">{message}</p>}
        {detail && (
          <>
            <h2 className="phone-queue-detail__name">{detail.name}</h2>
            <div className="phone-queue-detail__row">
              <QueueStatusChip status={detail.status} />
              <button type="button" onClick={() => void toggleRun(detail)} disabled={busy || lost}>
                {runLabel(detail.status)}
              </button>
            </div>
            {detail.status === 'Running' && (
              <>
                <QueuePauseControls queueId={detail.id} health={detail.health ?? null} onChanged={() => void loadDetail()} />
                <TemplateChangesNotice
                  queueId={detail.id}
                  hasTemplate={Boolean(detail.linkedTemplateId)}
                  paused={detail.health?.operatorPause === 'paused'}
                  refreshKey={templateSaves}
                  enabled={!lost}
                  onChanged={() => void loadDetail()}
                />
              </>
            )}
            <div className="phone-queue-activity" aria-label="Queue activity">
              {detailStale && <StaleMark />}
              <p className="phone-queue-activity__now" data-testid="queue-now">
                <span className="phone-queue-activity__label">Now: </span>{describeNow(monitor)}
              </p>
              {monitor?.lastOutcome && (
                <p className="phone-queue-activity__last" data-testid="queue-last-outcome">
                  <span className="phone-queue-activity__label">Last result: </span>
                  {monitor.lastOutcome.status}
                  {monitor.lastOutcome.summary ? ` - ${monitor.lastOutcome.summary}` : ''}
                </p>
              )}
            </div>
            <h3>Schedule</h3>
            {rows.length === 0 ? (
              <p className="form-hint">This queue has no entries.</p>
            ) : (
              <>
                {!template && (
                  <p className="form-hint">
                    {detail.linkedTemplateId
                      ? 'The linked template did not load. The switches are read-only.'
                      : 'No template is linked to this queue. The switches are read-only.'}
                  </p>
                )}
                <ul className="phone-card-list" aria-label="Queue schedule">
                  {rows.map((row) => (
                    <li key={row.key} className={row.enabled ? 'phone-card phone-schedule-row' : 'phone-card phone-schedule-row phone-schedule-row--disabled'}>
                      {row.entryId ? (
                        <button
                          type="button"
                          className="phone-card__body phone-schedule-row__open"
                          aria-label={`Entry settings for ${row.name}`}
                          onClick={() => openEntry(detail.id, row.entryId as string)}
                        >
                          <span className="phone-card__title" title={row.name}>{row.name}</span>
                          <span className="phone-card__summary" title={row.timingText}>
                            {row.enabled ? row.timingText : 'disabled'}
                          </span>
                          <span className="phone-schedule-row__chevron" aria-hidden="true">›</span>
                        </button>
                      ) : (
                        <div className="phone-card__body">
                          <span className="phone-card__title" title={row.name}>{row.name}</span>
                          <span className="phone-card__summary" title={row.timingText}>
                            {row.enabled ? row.timingText : 'disabled'}
                          </span>
                          {template?.needsUpgrade && <span className="phone-schedule-row__hint">{UPGRADE_HINT}</span>}
                        </div>
                      )}
                      {detail.status === 'Running' && row.enabled && row.entryId && (
                        <IconButton
                          icon="runNext"
                          label={`Run next ${row.name}`}
                          title={row.entryState === 'idle' ? 'Run next' : row.timingText}
                          size="touch"
                          disabled={busy || lost || row.entryState !== 'idle'}
                          onClick={() => void runNext(detail.id, row.entryId as string)}
                        />
                      )}
                      <input
                        type="checkbox"
                        role="switch"
                        aria-label={row.name}
                        checked={row.enabled}
                        disabled={!template || busy || lost}
                        readOnly={!template}
                        onChange={() => void toggleEntry(row.index)}
                      />
                    </li>
                  ))}
                </ul>
              </>
            )}
            {template && detail.status !== 'Running' && <p className="form-hint">Changes apply when the queue starts again.</p>}
            <p className="form-hint">Use a larger screen to edit schedules.</p>
          </>
        )}
      </section>
    );
  }

  return (
    <section className="phone-queues" data-testid="phone-queues">
      {message && <p className="form-hint" role="alert">{message}</p>}
      {listStale && <StaleMark />}
      {queues === null && <p role="status">Loading…</p>}
      {queues !== null && queues.length === 0 && !message && <p>No queues yet. Use a larger screen to create one.</p>}
      <ul className="phone-card-list">
        {(queues ?? []).map((q) => (
          <PhoneCard
            key={q.id}
            title={q.name}
            testId={`queue-card-${q.id}`}
            status={<QueueStatusChip status={q.status} />}
            summary={<>{buildQueueActivity(q, monitors[q.id])}{listStale ? ' (old data)' : ''}</>}
            onOpen={() => open(q.id)}
            action={{ label: runLabel(q.status), onClick: () => void toggleRun(q), disabled: busy || lost }}
          />
        ))}
      </ul>
    </section>
  );
};
