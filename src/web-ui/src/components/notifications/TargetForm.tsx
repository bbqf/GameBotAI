import React, { useEffect, useRef, useState } from 'react';
import { Button, FooterBar, IconButton, Toggle } from '../ui';
import { useLayoutMode } from '../../hooks/useLayoutMode';
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
  /** Asks to delete the target. The Delete button shows only for a saved target. */
  onDelete?: (target: NotificationTargetDto) => void;
};

const errorText = (err: unknown): string => (err instanceof Error ? err.message : 'The request failed.');

/**
 * Form to add or edit a Telegram target (feature 120). The token field is empty in the edit form.
 * An empty token field on save sends no token, so the API keeps the stored token. "Save and test"
 * saves first, and only a saved target gets the test message. A save error stops the test.
 */
export const TargetForm: React.FC<TargetFormProps> = ({ target, onSaved, onCancel, onDelete }) => {
  // On the phone, Delete is an icon button, so that the four footer actions fit on one line.
  const phone = useLayoutMode() === 'phone';
  const [name, setName] = useState('');
  const [chatId, setChatId] = useState('');
  const [botToken, setBotToken] = useState('');
  const [enabled, setEnabled] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [nameError, setNameError] = useState<string | null>(null);
  const [chatIdError, setChatIdError] = useState<string | null>(null);

  // The id of the target that this form saved. The page then passes it back, and the form keeps its values and message.
  const savedIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (target && target.id === savedIdRef.current) return;
    savedIdRef.current = null;
    setName(target?.name ?? '');
    setChatId(target?.settings?.chatId ?? '');
    setBotToken('');
    setEnabled(target?.enabled ?? true);
    setError(null);
    setMessage(null);
    setNameError(null);
    setChatIdError(null);
    // The form resets only when another target opens, not at each change of the saved target.
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
    savedIdRef.current = saved.id;
    onSaved(saved);
    return saved;
  };

  const validate = (): boolean => {
    const nameMissing = name.trim() === '';
    const chatMissing = chatId.trim() === '';
    setNameError(nameMissing ? 'Enter a name.' : null);
    setChatIdError(chatMissing ? 'Enter a chat ID.' : null);
    return !nameMissing && !chatMissing;
  };

  const run = async (withTest: boolean) => {
    setError(null);
    setMessage(null);
    if (!validate()) return;
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
      id="target-form"
      className="notification-target-form"
      aria-label={target ? 'Edit target' : 'Add target'}
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        void run(false);
      }}
    >
      <div className="ui-side-panel__form-content">
        {error && <div role="alert" className="form-error">{error}</div>}
        <section className="ui-form-card" aria-label="Target">
          <h4>Target</h4>
          <div className={`ui-field${nameError ? ' ui-field--error' : ''}`}>
            <label className="ui-field__label" htmlFor="target-name">Name</label>
            <input
              id="target-name"
              className="ui-field__input"
              value={name}
              maxLength={100}
              aria-invalid={nameError ? true : undefined}
              aria-describedby={nameError ? 'target-name-error' : undefined}
              onChange={(e) => { setName(e.target.value); setNameError(null); }}
            />
            {nameError && <div id="target-name-error" className="ui-field__error">{nameError}</div>}
          </div>
          <div className="ui-field">
            <label className="ui-field__label" htmlFor="target-token">Bot token</label>
            <input
              id="target-token"
              className="ui-field__input"
              type="password"
              autoComplete="off"
              value={botToken}
              placeholder={target?.hasSecret ? `Saved (${target.secretHint ?? ''}). Leave empty to keep it.` : ''}
              onChange={(e) => setBotToken(e.target.value)}
            />
          </div>
          <div className={`ui-field${chatIdError ? ' ui-field--error' : ''}`}>
            <label className="ui-field__label" htmlFor="target-chat">Chat ID</label>
            <input
              id="target-chat"
              className="ui-field__input"
              value={chatId}
              aria-invalid={chatIdError ? true : undefined}
              aria-describedby={chatIdError ? 'target-chat-error' : undefined}
              onChange={(e) => { setChatId(e.target.value); setChatIdError(null); }}
            />
            {chatIdError && <div id="target-chat-error" className="ui-field__error">{chatIdError}</div>}
          </div>
        </section>
        <section className="ui-form-card" aria-label="Status">
          <h4>Status</h4>
          <Toggle id="target-enabled" label="Enabled" checked={enabled} onChange={setEnabled} />
        </section>
        {message && <div role="status" className="message">{message}</div>}
      </div>
      <FooterBar
        left={target && onDelete ? (phone
          ? <IconButton icon="delete" label="Delete" tone="danger" disabled={busy} onClick={() => onDelete(target)} />
          : <Button variant="danger-outline" disabled={busy} onClick={() => onDelete(target)}>Delete</Button>) : undefined}
        right={(
          <>
            <Button variant="secondary" disabled={busy} onClick={onCancel}>Cancel</Button>
            <Button variant="secondary" disabled={busy} onClick={() => void run(true)}>Save and test</Button>
            <Button variant="primary" type="submit" disabled={busy}>Save</Button>
          </>
        )}
      />
    </form>
  );
};
