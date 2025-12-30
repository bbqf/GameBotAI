import React, { useEffect, useMemo, useState } from 'react';
import { listActions, ActionDto } from '../services/actionsApi';
import { useGames } from '../services/useGames';
import { createSession } from '../services/sessionsApi';
import { sessionCache } from '../lib/sessionCache';
import { ApiError } from '../lib/api';

const getAdbSerial = (action?: ActionDto): string => {
  if (!action) return '';
  const raw = action.attributes?.adbSerial;
  return typeof raw === 'string' ? raw : '';
};

export const ExecutionPage: React.FC = () => {
  const { data: gamesData, loading: gamesLoading, error: gamesError } = useGames();
  const [actions, setActions] = useState<ActionDto[]>([]);
  const [loadingActions, setLoadingActions] = useState(true);
  const [selectedActionId, setSelectedActionId] = useState<string | undefined>(undefined);
  const [running, setRunning] = useState(false);
  const [message, setMessage] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | undefined>(undefined);
  const [sessionId, setSessionId] = useState<string | undefined>(undefined);
  const [cachedSessionId, setCachedSessionId] = useState<string | undefined>(undefined);

  const gameLookup = useMemo(() => {
    const map = new Map<string, string>();
    (gamesData ?? []).forEach((g) => map.set(g.id, g.name));
    return map;
  }, [gamesData]);

  const connectActions = useMemo(() => actions.filter((a) => a.type === 'connect-to-game'), [actions]);
  const selectedAction = connectActions.find((a) => a.id === selectedActionId);

  useEffect(() => {
    const load = async () => {
      setLoadingActions(true);
      setError(undefined);
      try {
        const data = await listActions({ type: 'connect-to-game' });
        setActions(data.filter((a) => a.type === 'connect-to-game'));
      } catch (err: any) {
        setActions([]);
        setError(err?.message ?? 'Failed to load actions');
      } finally {
        setLoadingActions(false);
      }
    };
    void load();
  }, []);

  useEffect(() => {
    if (!selectedActionId && connectActions.length > 0) {
      setSelectedActionId(connectActions[0].id);
    }
  }, [connectActions, selectedActionId]);

  useEffect(() => {
    if (!selectedAction) {
      setCachedSessionId(undefined);
      return;
    }
    const adbSerial = getAdbSerial(selectedAction);
    if (!adbSerial) {
      setCachedSessionId(undefined);
      return;
    }
    setCachedSessionId(sessionCache.get(selectedAction.gameId, adbSerial));
  }, [selectedAction]);

  const handleRun = async () => {
    if (!selectedAction) return;
    const adbSerial = getAdbSerial(selectedAction);
    const gameId = selectedAction.gameId;
    if (!gameId || !adbSerial) {
      setError('Selected action is missing required game or adbSerial.');
      return;
    }

    setRunning(true);
    setMessage(undefined);
    setError(undefined);
    setSessionId(undefined);

    try {
      const response = await createSession({ gameId, adbSerial });
      const sid = response.sessionId || response.id;
      if (!sid) throw new ApiError(500, 'Session response missing id');
      sessionCache.set(gameId, adbSerial, sid);
      setSessionId(sid);
      setCachedSessionId(sid);
      setMessage(`Session ready: ${sid}`);
    } catch (err: any) {
      sessionCache.clear(gameId, adbSerial);
      setCachedSessionId(undefined);
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError(err?.message ?? 'Failed to start session');
      }
    } finally {
      setRunning(false);
    }
  };

  return (
    <div className="execution-view">
      <h1>Execution</h1>
      {gamesError && <div className="form-error" role="alert">{gamesError}</div>}
      {error && <div className="form-error" role="alert">{error}</div>}
      {message && <div className="form-hint" role="status">{message}</div>}

      <section aria-label="Connect to game">
        <h2>Start a session</h2>
        <p>Select a connect-to-game action and start a session. The returned sessionId will be cached for reuse.</p>

        <div className="field">
          <label htmlFor="connect-action-select">Connect action</label>
          <select
            id="connect-action-select"
            aria-label="Connect action"
            value={selectedActionId ?? ''}
            onChange={(e) => setSelectedActionId(e.target.value)}
            disabled={loadingActions || running || connectActions.length === 0}
          >
            {connectActions.map((a) => (
              <option key={a.id} value={a.id}>{a.name}</option>
            ))}
          </select>
          {loadingActions && <div className="form-hint">Loading connect actions...</div>}
          {!loadingActions && connectActions.length === 0 && <div className="form-hint">No connect-to-game actions available.</div>}
        </div>

        {selectedAction && (
          <div className="action-details">
            <div>Game: {gameLookup.get(selectedAction.gameId) ?? selectedAction.gameId}</div>
            <div>ADB Serial: {getAdbSerial(selectedAction) || '—'}</div>
            <div>Cached session: {cachedSessionId ?? 'none'}</div>
          </div>
        )}

        <div className="form-actions">
          <button type="button" onClick={handleRun} disabled={running || loadingActions || !selectedAction}>Start session</button>
        </div>
        {sessionId && <div className="form-hint">Active sessionId: {sessionId}</div>}
      </section>
    </div>
  );
};