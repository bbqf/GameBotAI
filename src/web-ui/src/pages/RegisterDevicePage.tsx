import React, { useState } from 'react';
import { isPasskeySupported } from '../lib/passkey';
import { registerPasskey } from '../services/authApi';
import '../components/auth/auth.css';

export const REGISTER_FAILURE_TEXT = 'Registration did not work. Make a new code and try again.';

type RegisterDevicePageProps = {
  /** Called after a correct registration. The service has made a session. */
  onRegistered: () => void;
};

/** The registration page at /register (contracts/ui-contract.md U-3). */
export const RegisterDevicePage: React.FC<RegisterDevicePageProps> = ({ onRegistered }) => {
  const supported = isPasskeySupported();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  const handleRegister = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setFailed(false);
    try {
      const result = await registerPasskey(code.trim(), name);
      if (result === 'registered') onRegistered();
      else if (result === 'failed') setFailed(true);
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="auth-page" aria-labelledby="register-title">
      <h1 id="register-title">Register this device</h1>
      {!supported && <p className="auth-error" role="alert">This browser does not support passkeys.</p>}
      <form onSubmit={handleRegister} className="auth-page">
        <label htmlFor="registration-code">
          Registration code
          <input
            id="registration-code"
            type="text"
            value={code}
            placeholder="XXXX-XXXX"
            autoCapitalize="characters"
            autoComplete="off"
            autoCorrect="off"
            spellCheck={false}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
          />
        </label>
        <label htmlFor="device-name">
          Device name
          <input
            id="device-name"
            type="text"
            value={name}
            maxLength={40}
            autoComplete="off"
            onChange={(e) => setName(e.target.value)}
          />
        </label>
        <button type="submit" className="auth-button" disabled={!supported || busy || code.trim().length === 0}>
          Register this device
        </button>
      </form>
      {failed && <p className="auth-error" role="alert">{REGISTER_FAILURE_TEXT}</p>}
      <p className="auth-help">
        Make a code on the GameBot computer with the command <code>GameBot.Service.exe auth new-code</code>.
      </p>
    </main>
  );
};
