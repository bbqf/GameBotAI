import React, { useEffect, useState } from 'react';
import { FormField } from '../FormField';
import {
  NotificationTargetDto,
  createNotificationTarget,
  testNotificationTarget,
  updateNotificationTarget
} from '../../services/notifications';

type TargetFormProps = {
  /** The saved target to edit, or null to add a new one. */
  target: NotificationTargetDto | null;
  /** Called after each successful save, with the saved target. */
  onSaved: (saved: NotificationTargetDto) => void;
  onCancel: () => void;
};

const errorText = (err: unknown): string => (err instanceof Error ? err.message : 'The request failed.');

/**
 * Form to add or edit a Telegram target (feature 120). The token field is empty in the edit form.
 * An empty token field on save sends no token, so the API keeps the stored token. "Save and test"
 * saves first, and only a saved target gets the test message. A save error stops the test.
 */
export const TargetForm: React.FC<TargetFormProps> = ({ target, onSaved, onCancel }) => {
  const [name, setName] = useState('');
  const [chatId, setChatId] = useState('');
  const [botToken, setBotToken] = useState('');
  const [enabled, setEnabled] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    setName(target?.name ?? '');
    setChatId(target?.settings?.chatId ?? '');
    setBotToken('');
    setEnabled(target?.enabled ?? true);
    setError(null);
    setMessage(null);
  }, [target?.id]);

  const save = async (): Promise<NotificationTargetDto> => {
    const token = botToken.trim();
    const input = {
      type: 'telegram',
      name: name.trim(),
      enabled,
      settings: { chatId: chatId.trim() },
      ...(token ? { secrets: { botToken: token } } : {})
    };
    const saved = target
      ? await updateNotificationTarget(target.id, input)
      : await createNotificationTarget(input);
    setBotToken('');
    onSaved(saved);
    return saved;
  };

  const run = async (withTest: boolean) => {
    setBusy(true);
    setError(null);
    setMessage(null);
    let saved: NotificationTargetDto;
    try {
      saved = await save();
    } catch (err) {
      setError(errorText(err));
      setBusy(false);
      return;
    }
    if (!withTest) {
      setMessage('The target is saved.');
      setBusy(false);
      return;
    }
    try {
      const result = await testNotificationTarget(saved.id);
      setMessage(
        result.ok
          ? 'The target is saved. The test message was sent.'
          : `The target is saved. The test message failed: ${result.reason ?? 'unknown reason'}`
      );
    } catch (err) {
      setMessage(`The target is saved. The test message failed: ${errorText(err)}`);
    }
    setBusy(false);
  };

  return (
    <form
      className="notification-target-form"
      aria-label={target ? 'Edit target' : 'Add target'}
      onSubmit={(e) => {
        e.preventDefault();
        void run(false);
      }}
    >
      <h3>{target ? 'Edit target' : 'Add target'}</h3>
      <FormField label="Name" htmlFor="target-name">
        <input id="target-name" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
      </FormField>
      <FormField label="Bot token" htmlFor="target-token">
        <input
          id="target-token"
          type="password"
          autoComplete="off"
          value={botToken}
          placeholder={target?.hasSecret ? `Saved (${target.secretHint ?? ''}). Leave empty to keep it.` : ''}
          onChange={(e) => setBotToken(e.target.value)}
        />
      </FormField>
      <FormField label="Chat ID" htmlFor="target-chat">
        <input id="target-chat" value={chatId} onChange={(e) => setChatId(e.target.value)} />
      </FormField>
      <FormField label="Enabled" htmlFor="target-enabled">
        <input id="target-enabled" type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
      </FormField>
      <div className="actions">
        <button type="submit" className="btn" disabled={busy}>Save</button>{' '}
        <button type="button" className="btn" disabled={busy} onClick={() => void run(true)}>Save and test</button>{' '}
        <button type="button" className="btn btn-secondary" onClick={onCancel}>Cancel</button>
      </div>
      {error && <div role="alert" className="form-error">{error}</div>}
      {message && <div role="status" className="message">{message}</div>}
    </form>
  );
};
