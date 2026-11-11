import React, { useCallback, useEffect, useState } from 'react';
import { refreshAuthStatus } from '../../lib/authStatus';
import {
  endAllSessions,
  listPasskeys,
  removePasskey,
  renamePasskey,
  signOut,
  type PasskeyItem
} from '../../services/authApi';
import './auth.css';

const formatTime = (value: string | null): string => (value ? new Date(value).toLocaleString() : 'Never');

export const REMOVE_THIS_DEVICE_WARNING =
  'This is the passkey of this device. After you remove it, you must use the GameBot computer to register again.';
export const REMOVE_LAST_DEVICE_WARNING =
  'This is the last passkey. After you remove it, you must use the GameBot computer to register again.';

/** The "Sign-in devices" section of the Configuration area (contracts/ui-contract.md U-4). */
export const SignInDevicesSection: React.FC = () => {
  const [items, setItems] = useState<PasskeyItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [renaming, setRenaming] = useState<string | null>(null);
  const [draftName, setDraftName] = useState('');
  const [confirmRemove, setConfirmRemove] = useState<string | null>(null);
  const [confirmEndAll, setConfirmEndAll] = useState(false);

  const load = useCallback(async () => {
    try {
      setItems(await listPasskeys());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The list of devices did not load.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  // After a sign-out or a removal of this device, the gate shows the sign-in page.
  const afterSessionEnd = async () => { await refreshAuthStatus(); };

  const saveName = async (id: string) => {
    try {
      await renamePasskey(id, draftName);
      setRenaming(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The name did not change.');
    }
  };

  const confirmedRemove = async (item: PasskeyItem) => {
    try {
      await removePasskey(item.id);
      setConfirmRemove(null);
      if (item.current) await afterSessionEnd();
      else await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The device did not go.');
    }
  };

  const removeWarning = (item: PasskeyItem): string | null => {
    if (item.current) return REMOVE_THIS_DEVICE_WARNING;
    if (items.length === 1) return REMOVE_LAST_DEVICE_WARNING;
    return null;
  };

  return (
    <div className="auth-devices">
      {error && <p className="auth-error" role="alert">{error}</p>}
      <ul className="auth-device-list">
        {items.map((item) => (
          <li key={item.id} className="auth-device">
            <strong>{item.name}{item.current ? ' (This device)' : ''}</strong>
            {item.current && <span className="auth-help">This device</span>}
            <span>Registered: {formatTime(item.createdUtc)}</span>
            <span>Last used: {formatTime(item.lastUsedUtc)}</span>
            {renaming === item.id ? (
              <div className="auth-device-actions">
                <label htmlFor={`rename-${item.id}`}>
                  Device name
                  <input id={`rename-${item.id}`} type="text" maxLength={40} value={draftName} onChange={(e) => setDraftName(e.target.value)} />
                </label>
                <button type="button" className="auth-button" onClick={() => void saveName(item.id)}>Save name</button>
                <button type="button" className="auth-button" onClick={() => setRenaming(null)}>Cancel</button>
              </div>
            ) : (
              <div className="auth-device-actions">
                <button type="button" className="auth-button" onClick={() => { setRenaming(item.id); setDraftName(item.name); }}>Rename</button>
                <button type="button" className="auth-button" onClick={() => setConfirmRemove(item.id)}>Remove</button>
              </div>
            )}
            {confirmRemove === item.id && (
              <div role="alertdialog" aria-label={`Remove ${item.name}`}>
                {removeWarning(item) && <p className="auth-error">{removeWarning(item)}</p>}
                <p>Remove this passkey?</p>
                <div className="auth-device-actions">
                  <button type="button" className="auth-button" onClick={() => void confirmedRemove(item)}>Confirm remove</button>
                  <button type="button" className="auth-button" onClick={() => setConfirmRemove(null)}>Cancel</button>
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>
      <div className="auth-device-actions">
        <button type="button" className="auth-button" onClick={() => void signOut().then(afterSessionEnd)}>Sign out</button>
        <button type="button" className="auth-button" onClick={() => setConfirmEndAll(true)}>Sign out all devices</button>
      </div>
      {confirmEndAll && (
        <div role="alertdialog" aria-label="Sign out all devices">
          <p>Sign out all devices? Each device must sign in again.</p>
          <div className="auth-device-actions">
            <button type="button" className="auth-button" onClick={() => void endAllSessions().then(afterSessionEnd)}>Confirm sign out all</button>
            <button type="button" className="auth-button" onClick={() => setConfirmEndAll(false)}>Cancel</button>
          </div>
        </div>
      )}
      <p className="auth-help">
        To add a device, make a code on the GameBot computer with the command <code>GameBot.Service.exe auth new-code</code>.
        Then open the register page on the new device.
      </p>
    </div>
  );
};
