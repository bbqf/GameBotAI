import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  UpdateAttempt,
  UpdateCheckResult,
  UpdateStatus,
  UpdateApiError,
  checkForUpdate,
  getUpdateStatus,
  installUpdate
} from '../services/update';
import { reloadPage } from '../lib/reload';

export const UPDATE_AREA_PATH = '/update';
export const UPDATE_POLL_INTERVAL_MS = 2000;

type Phase = 'idle' | 'checking' | 'confirming' | 'starting' | 'installing';

const stateText: Record<string, string> = {
  downloading: 'Downloading the update...',
  verifying: 'Checking the download...',
  installing: 'Installing the update...',
  restarting: 'Restarting GameBot...'
};

const messageOf = (err: unknown, fallback: string): string => (err instanceof Error ? err.message : fallback);
const hintOf = (err: unknown): string | null => (err instanceof UpdateApiError ? err.hint ?? null : null);

const FailureDetails: React.FC<{ attempt: UpdateAttempt }> = ({ attempt }) => (
  <div className="update-failure" role="alert">
    <p className="error">
      The update to {attempt.targetVersion} failed. The old version still works.
    </p>
    {attempt.errorCode && <p>Error code: {attempt.errorCode}</p>}
    {attempt.errorMessage && <p>{attempt.errorMessage}</p>}
    {attempt.errorHint && <p>{attempt.errorHint}</p>}
    {attempt.msiexecExitCode != null && <p>Installer exit code: {attempt.msiexecExitCode}</p>}
    {attempt.logPath && <p>Installer log: {attempt.logPath}</p>}
  </div>
);

export const UpdatePage: React.FC = () => {
  const [status, setStatus] = useState<UpdateStatus | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [check, setCheck] = useState<UpdateCheckResult | null>(null);
  const [phase, setPhase] = useState<Phase>('idle');
  const [actionError, setActionError] = useState<{ message: string; hint: string | null } | null>(null);
  const [attempt, setAttempt] = useState<UpdateAttempt | null>(null);
  const [reconnecting, setReconnecting] = useState(false);
  const [result, setResult] = useState<UpdateAttempt | null>(null);
  const attemptIdRef = useRef<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getUpdateStatus()
      .then((s) => {
        if (cancelled) return;
        setStatus(s);
        setCheck(s.lastCheck ?? null);
        if (s.lastResult) setResult(s.lastResult);
      })
      .catch((err) => {
        if (!cancelled) setLoadError(messageOf(err, 'The update state could not be loaded.'));
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleCheck = async () => {
    setActionError(null);
    setPhase('checking');
    try {
      const next = await checkForUpdate();
      setCheck(next);
    } catch (err) {
      setActionError({ message: messageOf(err, 'The check failed.'), hint: hintOf(err) });
    } finally {
      setPhase('idle');
    }
  };

  const handleConfirmInstall = async () => {
    if (!check?.latestVersion) return;
    setActionError(null);
    setPhase('starting');
    try {
      const accepted = await installUpdate(check.latestVersion);
      attemptIdRef.current = accepted.attemptId;
      setResult(null);
      setAttempt(null);
      setReconnecting(false);
      setPhase('installing');
    } catch (err) {
      setActionError({ message: messageOf(err, 'The update did not start.'), hint: hintOf(err) });
      setPhase('idle');
    }
  };

  const poll = useCallback(async () => {
    try {
      const next = await getUpdateStatus();
      setReconnecting(false);
      setStatus(next);
      const id = attemptIdRef.current;
      if (next.lastResult && next.lastResult.attemptId === id) {
        setResult(next.lastResult);
        setAttempt(null);
        setPhase('idle');
        // The new bot serves the new web UI. Reload the whole page so the browser loads it.
        if (next.lastResult.state === 'succeeded') reloadPage();
        return;
      }
      if (next.attempt && next.attempt.attemptId === id) {
        if (next.attempt.state === 'failed') {
          setResult(next.attempt);
          setAttempt(null);
          setPhase('idle');
          return;
        }
        setAttempt(next.attempt);
      }
    } catch {
      // The bot stops while it installs. Keep trying until it answers again.
      setReconnecting(true);
    }
  }, []);

  useEffect(() => {
    if (phase !== 'installing') return undefined;
    const timer = setInterval(() => {
      void poll();
    }, UPDATE_POLL_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [phase, poll]);

  const installedVersion = status?.installedVersion ?? check?.installedVersion ?? '';
  const checking = phase === 'checking';
  const busy = phase !== 'idle';
  const canInstall = check?.status === 'updateAvailable' && status?.canInstallHere !== false;
  const notes = check?.notes?.trim() ? check.notes : null;

  const progressText = reconnecting
    ? 'GameBot restarts. This page waits for it...'
    : stateText[attempt?.state ?? 'downloading'] ?? 'Working...';

  return (
    <div className="update-page">
      <section className="update-section">
        {loadError && <p className="error">{loadError}</p>}
        {installedVersion && <p>Installed version: <strong>{installedVersion}</strong></p>}

        {result && result.state === 'succeeded' && (
          <div className="update-success" role="status">
            <p>The update to {result.targetVersion} is done.</p>
            <button type="button" onClick={() => setResult(null)}>Close</button>
          </div>
        )}
        {result && result.state === 'failed' && (
          <>
            <FailureDetails attempt={result} />
            <button type="button" onClick={() => setResult(null)}>Close</button>
          </>
        )}

        <button type="button" onClick={handleCheck} disabled={busy}>
          {checking ? 'Checking...' : 'Check for Update'}
        </button>

        {actionError && (
          <div role="alert">
            <p className="error">{actionError.message}</p>
            {actionError.hint && <p>{actionError.hint}</p>}
          </div>
        )}

        {check?.status === 'upToDate' && <p>GameBot is up to date.</p>}

        {check?.status === 'checkFailed' && (
          <div role="alert">
            <p className="error">{check.error?.message ?? 'The check failed.'}</p>
            {check.error?.hint && <p>{check.error.hint}</p>}
          </div>
        )}

        {check?.status === 'updateAvailable' && (
          <div className="update-available">
            <p>A new version is available: <strong>{check.latestVersion}</strong></p>
            {notes && (
              <div className="update-notes">
                <h3>Release notes</h3>
                {/* Plain text only. React escapes the text, so markup in the notes is never run. */}
                <pre style={{ whiteSpace: 'pre-wrap' }}>{notes}</pre>
              </div>
            )}
            {canInstall && phase !== 'installing' && (
              <button type="button" onClick={() => setPhase('confirming')} disabled={busy}>
                Install update
              </button>
            )}
            {status && !status.canInstallHere && status.installBlockedReason === 'remote' && (
              <p className="warning">Install from the bot PC.</p>
            )}
            {status && !status.canInstallHere && status.installBlockedReason === 'notInstalled' && (
              <p className="warning">Update works only for an installed bot.</p>
            )}
          </div>
        )}

        {phase === 'confirming' && (
          <div className="confirm-dialog" role="dialog" aria-modal="true" aria-label="Confirm update">
            <h3>Install update {check?.latestVersion}</h3>
            <p className="warning">
              All active queues stop at once. Queues that resume on start begin again after the restart.
            </p>
            <div className="dialog-actions">
              <button type="button" onClick={() => setPhase('idle')}>Cancel</button>
              <button type="button" onClick={handleConfirmInstall}>Stop queues and install</button>
            </div>
          </div>
        )}

        {(phase === 'starting' || phase === 'installing') && (
          <p role="status" className="update-progress">
            {phase === 'starting' ? 'Starting the update...' : progressText}
          </p>
        )}
      </section>
    </div>
  );
};
