import React, { useCallback, useEffect, useState } from 'react';
import { TelegramSetupGuide } from '../components/notifications/TelegramSetupGuide';
import { TargetList } from '../components/notifications/TargetList';
import { TargetForm } from '../components/notifications/TargetForm';
import { QueueLevelTable } from '../components/notifications/QueueLevelTable';
import {
  NotificationTargetDto,
  deleteNotificationTarget,
  listNotificationTargets,
  testNotificationTarget
} from '../services/notifications';

export const NOTIFICATIONS_AREA_PATH = '/notifications';

/**
 * Notifications page (feature 120): a Telegram setup guide, the list of targets with add, edit and
 * delete, and the notification level of each queue.
 */
export const NotificationsPage: React.FC = () => {
  const [targets, setTargets] = useState<NotificationTargetDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // null = the form is closed, 'new' = add form, otherwise the target to edit
  const [editing, setEditing] = useState<NotificationTargetDto | 'new' | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setTargets((await listNotificationTargets()) ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The targets could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const remove = async (id: string) => {
    setError(null);
    try {
      await deleteNotificationTarget(id);
      setTargets((prev) => prev.filter((t) => t.id !== id));
      setEditing((current) => (current !== null && current !== 'new' && current.id === id ? null : current));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The target could not be deleted.');
    }
  };

  const test = async (id: string) => {
    setNotice(null);
    try {
      const result = await testNotificationTarget(id);
      setNotice(result.ok ? 'The test message was sent.' : `The test message failed: ${result.reason ?? 'unknown reason'}`);
    } catch (err) {
      setNotice(`The test message failed: ${err instanceof Error ? err.message : 'unknown reason'}`);
    }
  };

  return (
    <section className="notifications-page" aria-label="Notifications">
      <p>
        GameBot sends a message to each enabled target when a queue sequence ends. Set the level of each
        queue in the table below. A manual run sends no message.
      </p>
      <TelegramSetupGuide />
      <h3>Targets</h3>
      {loading && <p>Wait. The targets load.</p>}
      {error && <div role="alert" className="form-error">{error}</div>}
      {notice && <div role="status" className="message">{notice}</div>}
      {!loading && (
        <TargetList
          targets={targets}
          onEdit={(target) => setEditing(target)}
          onDelete={(id) => void remove(id)}
          onTest={(id) => void test(id)}
        />
      )}
      {editing === null ? (
        <div className="actions">
          <button type="button" className="btn" onClick={() => setEditing('new')}>Add target</button>
        </div>
      ) : (
        <TargetForm
          target={editing === 'new' ? null : editing}
          onSaved={(saved) => {
            setTargets((prev) => (prev.some((t) => t.id === saved.id)
              ? prev.map((t) => (t.id === saved.id ? saved : t))
              : [...prev, saved]));
            setEditing(saved);
          }}
          onCancel={() => setEditing(null)}
        />
      )}
      <QueueLevelTable />
    </section>
  );
};
