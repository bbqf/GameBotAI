import React, { useCallback, useEffect, useRef, useState } from 'react';
import { PhoneCard } from '../phone/PhoneCard';
import { ConnectionState } from '../../hooks/useConnectionState';
import { toggleTemplateEntry } from '../../lib/queueEntryToggle';
import { ApiError } from '../../lib/api';
import {
  getQueue,
  listQueues,
  startQueue,
  stopQueue,
  QueueDetailDto,
  QueueDto
} from '../../services/queues';
import { getQueueTemplate, saveQueueTemplate, QueueTemplateDetail } from '../../services/queueTemplates';
import '../../features/execution/StatusChip.css';

export type PhoneQueuesViewProps = {
  connectionState?: ConnectionState;
  /** Change this number to reload the data (for example after the connection returns). */
  reloadSignal?: number;
  /** Change this number to return to the list. */
  resetSignal?: number;
};

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

/** Phone view of the queues: cards, start and stop, and the entry enable switches (contract C-08). */
export const PhoneQueuesView: React.FC<PhoneQueuesViewProps> = ({ connectionState = 'online', reloadSignal = 0, resetSignal = 0 }) => {
  const [queues, setQueues] = useState<QueueDto[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<QueueDetailDto | null>(null);
  const [template, setTemplate] = useState<QueueTemplateDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const selectedRef = useRef<string | null>(null);
  selectedRef.current = selectedId;

  const lost = connectionState === 'lost';

  const loadDetail = useCallback(async (id: string) => {
    const d = await getQueue(id);
    if (selectedRef.current !== id) return;
    setDetail(d);
    if (d.linkedTemplateId) {
      try {
        const t = await getQueueTemplate(d.linkedTemplateId);
        if (selectedRef.current === id) setTemplate(t);
      } catch (err) {
        if (selectedRef.current === id) {
          setTemplate(null);
          setMessage(errorText(err));
        }
      }
    } else {
      setTemplate(null);
    }
  }, []);

  const loadList = useCallback(async () => {
    try {
      const data = await listQueues();
      setQueues(Array.isArray(data) ? data : []);
    } catch (err) {
      setMessage(errorText(err));
      setQueues((prev) => prev ?? []);
    }
  }, []);

  useEffect(() => {
    void loadList();
    if (selectedRef.current) void loadDetail(selectedRef.current).catch((err) => setMessage(errorText(err)));
  }, [reloadSignal, loadList, loadDetail]);

  useEffect(() => {
    if (resetSignal === 0) return;
    setSelectedId(null);
    setDetail(null);
    setTemplate(null);
  }, [resetSignal]);

  const open = async (id: string) => {
    setSelectedId(id);
    selectedRef.current = id;
    setDetail(null);
    setTemplate(null);
    setMessage(null);
    try {
      await loadDetail(id);
    } catch (err) {
      setMessage(errorText(err));
    }
  };

  const close = () => {
    setSelectedId(null);
    setDetail(null);
    setTemplate(null);
    setMessage(null);
    void loadList();
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
    } catch (err) {
      setMessage(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  const runLabel = (status: string) => (status === 'Running' ? 'Stop' : 'Start');

  if (selectedId) {
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
            <h3>Entries</h3>
            {template ? (
              <ul className="phone-card-list" aria-label="Queue entries">
                {template.entries.map((entry, index) => {
                  const name = entry.sequenceName ?? entry.sequenceId;
                  const enabled = entry.enabled ?? true;
                  return (
                    <li key={`${entry.sequenceId}-${index}`} className="phone-card">
                      <div className="phone-card__body">
                        <span className="phone-card__title" title={name}>{name}</span>
                      </div>
                      <input
                        type="checkbox"
                        role="switch"
                        aria-label={name}
                        checked={enabled}
                        disabled={busy || lost}
                        onChange={() => void toggleEntry(index)}
                      />
                    </li>
                  );
                })}
              </ul>
            ) : (
              <>
                <p className="form-hint">
                  {detail.linkedTemplateId
                    ? 'The linked template did not load. The switches are read-only.'
                    : 'No template is linked to this queue. The switches are read-only.'}
                </p>
                <ul className="phone-card-list" aria-label="Queue entries">
                  {detail.entries.map((entry) => {
                    const name = entry.sequenceName ?? entry.sequenceId;
                    return (
                      <li key={entry.entryId} className="phone-card">
                        <span className="phone-card__title" title={name}>{name}</span>
                        <input type="checkbox" role="switch" aria-label={name} checked disabled readOnly />
                      </li>
                    );
                  })}
                </ul>
              </>
            )}
            {template && <p className="form-hint">Changes apply when the queue starts again.</p>}
            <p className="form-hint">Use a larger screen to edit schedules.</p>
          </>
        )}
      </section>
    );
  }

  return (
    <section className="phone-queues" data-testid="phone-queues">
      {message && <p className="form-hint" role="alert">{message}</p>}
      {queues === null && <p role="status">Loading…</p>}
      {queues !== null && queues.length === 0 && !message && <p>No queues yet. Use a larger screen to create one.</p>}
      <ul className="phone-card-list">
        {(queues ?? []).map((q) => (
          <PhoneCard
            key={q.id}
            title={q.name}
            testId={`queue-card-${q.id}`}
            status={<QueueStatusChip status={q.status} />}
            onOpen={() => void open(q.id)}
            action={{ label: runLabel(q.status), onClick: () => void toggleRun(q), disabled: busy || lost }}
          />
        ))}
      </ul>
    </section>
  );
};
