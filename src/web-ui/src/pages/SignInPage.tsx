import React, { useState } from 'react';
import { isPasskeySupported } from '../lib/passkey';
import { signInWithPasskey } from '../services/authApi';
import '../components/auth/auth.css';

export const SIGN_IN_FAILURE_TEXT = 'Sign-in did not work. Try again.';

type SignInPageProps = {
  /** Called after a correct sign-in. */
  onSignedIn: () => void;
};

/** The sign-in page (contracts/ui-contract.md U-2). It has no password field, no user name, and no registration link. */
export const SignInPage: React.FC<SignInPageProps> = ({ onSignedIn }) => {
  const supported = isPasskeySupported();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  const handleSignIn = async () => {
    setBusy(true);
    setFailed(false);
    try {
      const result = await signInWithPasskey();
      if (result === 'signedIn') onSignedIn();
      else if (result === 'failed') setFailed(true);
      // A cancel of the system dialog shows no error.
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="auth-page" aria-labelledby="sign-in-title">
      <h1 id="sign-in-title">GameBot</h1>
      {!supported && <p className="auth-error" role="alert">This browser does not support passkeys.</p>}
      <button type="button" className="auth-button" onClick={handleSignIn} disabled={!supported || busy}>
        Sign in with passkey
      </button>
      {failed && <p className="auth-error" role="alert">{SIGN_IN_FAILURE_TEXT}</p>}
    </main>
  );
};
