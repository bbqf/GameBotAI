import React, { useCallback, useEffect, useState } from 'react';
import { TelegramSetupGuide } from '../components/notifications/TelegramSetupGuide';
import { TargetList } from '../components/notifications/TargetList';
import { TargetEditor } from '../components/notifications/TargetEditor';
import { QueueLevelTable } from '../components/notifications/QueueLevelTable';
import { Button, ConfirmDialog, PageHeader, SectionHeader, type ListState } from '../components/ui';
import { useLayoutMode } from '../hooks/useLayoutMode';
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
  const [loadError, setLoadError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // null = the form is closed, 'new' = add form, otherwise the target to edit
  const [editing, setEditing] = useState<NotificationTargetDto | 'new' | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<NotificationTargetDto | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [testingId, setTestingId] = useState<string | null>(null);
  const phone = useLayoutMode() === 'phone';

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      setTargets((await listNotificationTargets()) ?? []);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'The targets could not be loaded.');
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

  const confirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    await remove(deleteTarget.id);
    setDeleting(false);
    setDeleteTarget(null);
  };

  const test = async (id: string) => {
    setNotice(null);
    setTestingId(id);
    try {
      const result = await testNotificationTarget(id);
      setNotice(result.ok ? 'The test message was sent.' : `The test message failed: ${result.reason ?? 'unknown reason'}`);
    } catch (err) {
      setNotice(`The test message failed: ${err instanceof Error ? err.message : 'unknown reason'}`);
    } finally {
      setTestingId(null);
    }
  };

  let listState: ListState = 'ready';
  if (loading) listState = { kind: 'loading' };
  else if (loadError) listState = { kind: 'error', text: loadError, onRetry: () => void load() };
  else if (targets.length === 0) listState = { kind: 'empty', text: 'No target is saved. Follow the guide and add one.' };

  const editor = (
    <TargetEditor
      editing={editing}
      onSaved={(saved) => {
        setTargets((prev) => (prev.some((t) => t.id === saved.id)
          ? prev.map((t) => (t.id === saved.id ? saved : t))
          : [...prev, saved]));
        setEditing(saved);
      }}
      onClose={() => setEditing(null)}
      onDelete={(t) => setDeleteTarget(t)}
    />
  );

  const dialog = (
    <ConfirmDialog
      open={deleteTarget !== null}
      title={`Delete target ${deleteTarget?.name ?? ''}?`}
      message="The target gets no more messages. You cannot undo this."
      confirmText="Delete"
      danger
      busy={deleting}
      onConfirm={() => void confirmDelete()}
      onCancel={() => setDeleteTarget(null)}
    />
  );

  // On the phone the form is a full page: the rest of the page does not show.
  if (phone && editing !== null) {
    return (
      <section className="notifications-page" aria-label="Notifications">
        {editor}
        {dialog}
      </section>
    );
  }

  // The side panel covers the right edge. The page keeps a free space there, and the target list shows only its main
  // columns, so all rows and their actions stay visible with no sideways scroll (rule D2).
  return (
    <section className={`notifications-page${editing !== null ? ' notifications-page--panel-open' : ''}`} aria-label="Notifications">
      <PageHeader
        title="Alerts"
        actions={<Button variant="primary" icon="add" onClick={() => setEditing('new')}>Add target</Button>}
      />
      {error && <div role="alert" className="form-error">{error}</div>}
      {notice && <div role="status" className="message">{notice}</div>}
      <p>
        GameBot sends a message to each enabled target when a queue sequence ends. Set the level of each
        queue in the table below. A manual run sends no message.
      </p>
      <TelegramSetupGuide />
      <SectionHeader title="Targets" />
      <TargetList
        targets={targets}
        state={listState}
        onEdit={(target) => setEditing(target)}
        onDelete={(target) => setDeleteTarget(target)}
        onTest={(id) => void test(id)}
        testingId={testingId}
        compact={editing !== null}
        selectedId={editing !== null && editing !== 'new' ? editing.id : null}
      />
      <QueueLevelTable />
      {editor}
      {dialog}
    </section>
  );
};
