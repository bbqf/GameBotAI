import React, { useEffect, useMemo, useState } from 'react';
import { listActions, ActionDto } from '../services/actionsApi';
import { useGames } from '../services/useGames';
import { createSession } from '../services/sessionsApi';
import { listCommands, forceExecuteCommand, CommandDto } from '../services/commands';
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
  const [commands, setCommands] = useState<CommandDto[]>([]);
  const [loadingActions, setLoadingActions] = useState(true);
  const [loadingCommands, setLoadingCommands] = useState(true);
  const [selectedActionId, setSelectedActionId] = useState<string | undefined>(undefined);
  const [selectedCommandId, setSelectedCommandId] = useState<string | undefined>(undefined);
  const [running, setRunning] = useState(false);
  const [message, setMessage] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | undefined>(undefined);
  const [sessionId, setSessionId] = useState<string | undefined>(undefined);
  const [cachedSessionId, setCachedSessionId] = useState<string | undefined>(undefined);
  const [commandsError, setCommandsError] = useState<string | undefined>(undefined);
  const [commandMessage, setCommandMessage] = useState<string | undefined>(undefined);
  const [commandError, setCommandError] = useState<string | undefined>(undefined);
  const [manualSessionId, setManualSessionId] = useState('');
  const [executing, setExecuting] = useState(false);
  const [commandCachedSessionId, setCommandCachedSessionId] = useState<string | undefined>(undefined);
  const [commandCacheMeta, setCommandCacheMeta] = useState<{ gameId: string; adbSerial: string; actionName?: string } | undefined>(undefined);

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
    const load = async () => {
      setLoadingCommands(true);
      setCommandsError(undefined);
      try {
        const data = await listCommands();
        setCommands(data);
      } catch (err: any) {
        setCommands([]);
        setCommandsError(err?.message ?? 'Failed to load commands');
      } finally {
        setLoadingCommands(false);
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
    if (!selectedCommandId && commands.length > 0) {
      setSelectedCommandId(commands[0].id);
    }
  }, [commands, selectedCommandId]);

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

  const findCommandCacheMeta = (commandId?: string) => {
    if (!commandId) return undefined;
    const cmd = commands.find((c) => c.id === commandId);
    if (!cmd || !cmd.steps || cmd.steps.length === 0) return undefined;
    const firstActionStep = [...cmd.steps].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).find((s) => s.type === 'Action');
    if (!firstActionStep) return undefined;
    const action = connectActions.find((a) => a.id === firstActionStep.targetId);
    if (!action || action.type !== 'connect-to-game') return undefined;
    const adbSerial = getAdbSerial(action);
    const gameId = typeof action.gameId === 'string' && action.gameId ? action.gameId : (typeof action.attributes?.gameId === 'string' ? action.attributes.gameId : '');
    if (!gameId || !adbSerial) return undefined;
    return { gameId, adbSerial, actionName: action.name };
  };

  useEffect(() => {
    const meta = findCommandCacheMeta(selectedCommandId);
    setCommandCacheMeta(meta);
    setCommandCachedSessionId(meta ? sessionCache.get(meta.gameId, meta.adbSerial) : undefined);
  }, [selectedCommandId, commands, connectActions]);

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

  const handleExecuteCommand = async () => {
    if (!selectedCommandId) {
      setCommandError('Select a command to execute.');
      return;
    }

    setExecuting(true);
    setCommandMessage(undefined);
    setCommandError(undefined);

    let resolvedSessionId = manualSessionId.trim();
    const meta = findCommandCacheMeta(selectedCommandId);
    if (!resolvedSessionId) {
      if (!meta) {
        setExecuting(false);
        setCommandError('No connect-to-game step found for this command. Enter a sessionId or update the command to include one.');
        return;
      }
      const cached = sessionCache.get(meta.gameId, meta.adbSerial);
      if (!cached) {
        setExecuting(false);
        setCommandError(`No cached session found for ${meta.gameId}/${meta.adbSerial}. Start a session first or enter a sessionId.`);
        return;
      }
      resolvedSessionId = cached;
    }

    try {
      const result = await forceExecuteCommand(selectedCommandId, resolvedSessionId);
      const accepted = typeof result?.accepted === 'number' ? result.accepted : undefined;
      setCommandMessage(accepted !== undefined ? `Command accepted: ${accepted}` : 'Command submitted');
    } catch (err: any) {
      if (err instanceof ApiError) {
        setCommandError(err.message);
      } else {
        setCommandError(err?.message ?? 'Failed to execute command');
      }
    } finally {
      setExecuting(false);
    }
  };

  return (
    <div className="execution-view">
      <h1>Execution</h1>
      {gamesError && <div className="form-error" role="alert">{gamesError}</div>}
      {error && <div className="form-error" role="alert">{error}</div>}
      {message && <div className="form-hint" role="status">{message}</div>}
      {commandError && <div className="form-error" role="alert">{commandError}</div>}
      {commandMessage && <div className="form-hint" role="status">{commandMessage}</div>}

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

      <section aria-label="Execute command">
        <h2>Execute a command</h2>
        <p>Reuse the cached sessionId from a connect-to-game action when left blank.</p>

        <div className="field">
          <label htmlFor="command-select">Command</label>
          <select
            id="command-select"
            aria-label="Command"
            value={selectedCommandId ?? ''}
            onChange={(e) => { setSelectedCommandId(e.target.value); setCommandMessage(undefined); setCommandError(undefined); }}
            disabled={loadingCommands || executing || commands.length === 0}
          >
            {commands.map((c) => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
          {loadingCommands && <div className="form-hint">Loading commands...</div>}
          {!loadingCommands && commands.length === 0 && <div className="form-hint">No commands available.</div>}
          {commandsError && <div className="form-error" role="alert">{commandsError}</div>}
        </div>

        {commandCacheMeta && (
          <div className="action-details">
            <div>Connect action: {commandCacheMeta.actionName ?? 'connect-to-game'}</div>
            <div>Game: {gameLookup.get(commandCacheMeta.gameId) ?? commandCacheMeta.gameId}</div>
            <div>ADB Serial: {commandCacheMeta.adbSerial}</div>
            <div>Cached session: {commandCachedSessionId ?? 'none'}</div>
          </div>
        )}

        <div className="field">
          <label htmlFor="command-session">SessionId (optional)</label>
          <input
            id="command-session"
            aria-label="SessionId"
            value={manualSessionId}
            onChange={(e) => setManualSessionId(e.target.value)}
            placeholder="Leave blank to use cached session"
            disabled={executing || loadingCommands}
          />
        </div>

        <div className="form-actions">
          <button type="button" onClick={handleExecuteCommand} disabled={executing || loadingCommands || commands.length === 0}>Execute command</button>
        </div>
      </section>
    </div>
  );
};