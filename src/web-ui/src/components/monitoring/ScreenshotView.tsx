import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useSequentialPolling } from '../../hooks/useSequentialPolling';
import { fetchSnapshot } from '../../services/monitoringApi';

/** The time from the start of one screenshot request to the start of the next one (FR-005). */
export const SCREENSHOT_INTERVAL_MS = 2000;

export type ScreenshotViewProps = {
  /** The selected session. Only this session is requested. */
  sessionId: string;
  sessionName: string;
  /** False pauses the requests, for example while the connection is lost. */
  enabled?: boolean;
};

const errorText = (err: unknown): string => (err instanceof Error ? err.message : 'The screenshot request failed.');

/**
 * Live picture of one session (feature 141, contract M-20 to M-25). It requests one screenshot at a time, keeps
 * the last good picture when a request fails, and counts the age of the picture each second.
 */
export const ScreenshotView: React.FC<ScreenshotViewProps> = ({ sessionId, sessionName, enabled = true }) => {
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [capturedAt, setCapturedAt] = useState<number | null>(null);
  const [failed, setFailed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const urlRef = useRef<string | null>(null);

  const dropPicture = useCallback(() => {
    if (urlRef.current) {
      URL.revokeObjectURL(urlRef.current);
      urlRef.current = null;
    }
  }, []);

  // A new session clears the picture of the old session.
  useEffect(() => {
    dropPicture();
    setImageUrl(null);
    setCapturedAt(null);
    setFailed(false);
    setError(null);
    return dropPicture;
  }, [sessionId, dropPicture]);

  const load = useCallback(async (signal: AbortSignal) => {
    try {
      const blob = await fetchSnapshot(sessionId, signal);
      if (signal.aborted) return;
      const next = URL.createObjectURL(blob);
      const previous = urlRef.current;
      urlRef.current = next;
      setImageUrl(next);
      setCapturedAt(Date.now());
      setFailed(false);
      setError(null);
      if (previous) URL.revokeObjectURL(previous);
    } catch (err) {
      if (signal.aborted) return;
      setFailed(true);
      setError(errorText(err));
    }
  }, [sessionId]);

  useSequentialPolling(load, { intervalMs: SCREENSHOT_INTERVAL_MS, enabled, resetKey: sessionId });

  useEffect(() => {
    if (capturedAt === null) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [capturedAt]);

  const ageSeconds = capturedAt === null ? 0 : Math.max(0, Math.floor((now - capturedAt) / 1000));

  return (
    <div className="monitoring-screenshot" data-testid="monitoring-screenshot">
      {imageUrl === null && !failed && <p role="status">Loading</p>}
      {failed && (
        <p className="monitoring-screenshot__marker" role="alert" data-testid="no-live-picture">
          No live picture
        </p>
      )}
      {failed && imageUrl === null && error && <p className="form-hint">{error}</p>}
      {imageUrl !== null && (
        <>
          <img className="monitoring-screenshot__image" src={imageUrl} alt={`Live screenshot of ${sessionName}`} />
          <p className="form-hint" data-testid="screenshot-age">Updated {ageSeconds} s ago</p>
        </>
      )}
    </div>
  );
};
