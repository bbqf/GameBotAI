import React, { useCallback, useEffect, useState } from 'react';
import { deriveGateView, refreshAuthStatus, useAuthStatus } from '../../lib/authStatus';
import { clearUnauthorized } from '../../lib/unauthorized';
import { setToken } from '../../lib/token';
import { RegisterDevicePage } from '../../pages/RegisterDevicePage';
import { SignInPage } from '../../pages/SignInPage';
import './auth.css';

export const REFUSED_TEXT = 'This service does not accept connections from the internet.';
export const OTHER_ADDRESS_TEXT = 'Passkey sign-in works only at the public address.';

const isRegisterPath = (): boolean => /^\/register\/?$/i.test(window.location.pathname);

/** The access token prompt for the direct address (feature 187, FR-031). It shows only when the service accepts a token here. */
const TokenPrompt: React.FC<{ onSaved: () => void }> = ({ onSaved }) => {
  const [value, setValue] = useState('');
  return (
    <form
      className="auth-page"
      onSubmit={(e) => {
        e.preventDefault();
        setToken(value);
        onSaved();
      }}
    >
      <label htmlFor="access-token">
        Access token
        <input id="access-token" type="password" value={value} autoComplete="off" onChange={(e) => setValue(e.target.value)} />
      </label>
      <button type="submit" className="auth-button" disabled={value.length === 0}>Use token</button>
    </form>
  );
};

/**
 * The access gate of the web UI (contracts/ui-contract.md U-1). With the sign-in function off, it shows the
 * content as before. The address does not change at sign-in, so the page that the owner asked for shows afterward.
 */
export const AccessGate: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const state = useAuthStatus();
  const [registering, setRegistering] = useState<boolean>(isRegisterPath);
  // A new value makes the content mount again, so it loads its data again after the sign-in (FR-021).
  const [epoch, setEpoch] = useState(0);

  useEffect(() => { void refreshAuthStatus(); }, []);

  const afterAccess = useCallback(async () => {
    clearUnauthorized();
    await refreshAuthStatus();
    setEpoch((n) => n + 1);
  }, []);

  const handleRegistered = useCallback(async () => {
    window.history.replaceState(null, '', '/');
    setRegistering(false);
    await afterAccess();
  }, [afterAccess]);

  const view = deriveGateView(state);
  if (view === 'loading') return null;
  if (view === 'refused') return <main className="auth-page"><p role="alert">{REFUSED_TEXT}</p></main>;
  if (registering && state.status?.signInEnabled) return <RegisterDevicePage onRegistered={handleRegistered} />;
  if (view === 'signIn') return <SignInPage onSignedIn={afterAccess} />;
  if (view === 'otherAddress') {
    const status = state.status;
    return (
      <main className="auth-page">
        <p>{OTHER_ADDRESS_TEXT}</p>
        {status?.publicOrigin && <p><a href={status.publicOrigin}>{status.publicOrigin}</a></p>}
        {status?.tokenAccessHere && <TokenPrompt onSaved={afterAccess} />}
      </main>
    );
  }
  return <React.Fragment key={epoch}>{children}</React.Fragment>;
};
