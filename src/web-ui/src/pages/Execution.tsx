import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { listActions, ActionDto } from '../services/actionsApi';
import { useGames } from '../services/useGames';
import { getRunningSessions, startSession, stopSession, RunningSessionDto } from '../services/sessionsApi';
import { listCommands, forceExecuteCommand, CommandDto } from '../services/commands';
import { ApiError } from '../lib/api';
import { StatusChip } from '../features/execution/StatusChip';
import { RunDetails } from '../features/execution/RunDetails';

const getAdbSerial = (action?: ActionDto): string => {
  if (!action) return '';
  const raw = action.attributes?.adbSerial;
  return typeof raw === 'string' ? raw : '';
};

export const ExecutionPage: React.FC = () => {
  const { data: gamesData, error: gamesError } = useGames();
  const [actions, setActions] = useState<ActionDto[]>([]);
  const [commands, setCommands] = useState<CommandDto[]>([]);
  const [loadingActions, setLoadingActions] = useState(true);
  const [loadingCommands, setLoadingCommands] = useState(true);
  const [selectedActionId, setSelectedActionId] = useState<string | undefined>(undefined);
  const [selectedCommandId, setSelectedCommandId] = useState<string | undefined>(undefined);
  const [running, setRunning] = useState(false);
  const [message, setMessage] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | undefined>(undefined);
  const [runningSessions, setRunningSessions] = useState<RunningSessionDto[]>([]);
  const [runningSessionsLoading, setRunningSessionsLoading] = useState(true);
  const [commandsError, setCommandsError] = useState<string | undefined>(undefined);
  const [commandMessage, setCommandMessage] = useState<string | undefined>(undefined);
  const [commandError, setCommandError] = useState<string | undefined>(undefined);
  const [manualSessionId, setManualSessionId] = useState('');
  const [executing, setExecuting] = useState(false);
  const [commandCacheMeta, setCommandCacheMeta] = useState<{ gameId: string; adbSerial: string; actionName?: string } | undefined>(undefined);

  const gameLookup = useMemo(() => {
    const map = new Map<string, string>();
    (gamesData ?? []).forEach((g) => map.set(g.id, g.name));
    return map;
  }, [gamesData]);

  const connectActions = useMemo(() => actions.filter((a) => a.type === 'connect-to-game'), [actions]);
  const selectedAction = connectActions.find((a) => a.id === selectedActionId);

  const refreshRunningSessions = useCallback(async () => {
    setRunningSessionsLoading(true);
    try {
      const data = await getRunningSessions();
      if (Array.isArray(data)) {
        setRunningSessions(data);
      } else {
        setRunningSessions([]);
        setError('Unexpected response while loading running sessions');
      }
    } catch (err: any) {
      setRunningSessions([]);
      setError(err?.message ?? 'Failed to load running sessions');
    } finally {
      setRunningSessionsLoading(false);
    }
  }, []);

  useEffect(() => {
    const load = async () => {
      setLoadingActions(true);
      setError(undefined);
      try {
        const data = await listActions({ type: 'connect-to-game' });
        const safe = Array.isArray(data) ? data : [];
        setActions(safe.filter((a) => a.type === 'connect-to-game'));
        if (!Array.isArray(data)) {
          setError('Unexpected response while loading actions');
        }
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
        if (Array.isArray(data)) {
          setCommands(data);
        } else {
          setCommands([]);
          setCommandsError('Unexpected response while loading commands');
        }
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
    void refreshRunningSessions();
  }, [refreshRunningSessions]);

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
    return { gameId, adbSerial, actionName: action.name };
  };

  useEffect(() => {
    const meta = findCommandCacheMeta(selectedCommandId);
    setCommandCacheMeta(meta);
  }, [selectedCommandId, commands, connectActions]);

  const selectedRunningSession = useMemo(() => {
    if (!selectedAction) return undefined;
    const adbSerial = getAdbSerial(selectedAction);
    if (!adbSerial) return undefined;
    return runningSessions.find((s) => s.gameId === selectedAction.gameId && s.emulatorId === adbSerial);
  }, [selectedAction, runningSessions]);

  const commandRunningSession = useMemo(() => {
    if (!commandCacheMeta) return undefined;
    return runningSessions.find((s) => s.gameId === commandCacheMeta.gameId && s.emulatorId === commandCacheMeta.adbSerial);
  }, [commandCacheMeta, runningSessions]);

  const cachedSession = commandRunningSession ?? selectedRunningSession ?? runningSessions[0];

  const primaryRunDetails = commandRunningSession ?? selectedRunningSession ?? runningSessions[0];

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

    try {
      const response = await startSession({ gameId, emulatorId: adbSerial });
      const runningList = Array.isArray(response.runningSessions) ? response.runningSessions : [];
      if (!Array.isArray(response.runningSessions)) {
        setError('Unexpected response while starting session');
      }
      setRunningSessions(runningList);
      setMessage(`Session ready: ${response.sessionId ?? ''}`.trim());
    } catch (err: any) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError(err?.message ?? 'Failed to start session');
      }
    } finally {
      setRunning(false);
      void refreshRunningSessions();
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
      const runningSession = runningSessions.find((s) => s.gameId === meta.gameId && s.emulatorId === meta.adbSerial && `${s.status}`.toLowerCase() === 'running');
      if (!runningSession) {
        setExecuting(false);
        setCommandError(`No running session found for ${meta.gameId}/${meta.adbSerial}. Start a session first or enter a sessionId.`);
        return;
      }
      resolvedSessionId = runningSession.sessionId;
    }

    try {
      const result = await forceExecuteCommand(selectedCommandId, resolvedSessionId);
      const accepted = typeof result?.accepted === 'number' ? result.accepted : undefined;
      setCommandMessage(accepted !== undefined ? `Command accepted: ${accepted}` : 'Command submitted');
    } catch (err: any) {
      if (err instanceof ApiError) {
        setCommandError(err.message);
        if (err.status === 404 || err.status === 409) {
          void refreshRunningSessions();
        }
      } else {
        setCommandError(err?.message ?? 'Failed to execute command');
      }
    } finally {
      setExecuting(false);
    }
  };

  const handleStopSession = async (sessionId: string) => {
    if (!sessionId) return;
    setError(undefined);
    try {
      await stopSession(sessionId);
      setMessage('Session stopped.');
    } catch (err: any) {
      if (err instanceof ApiError) setError(err.message);
      else setError(err?.message ?? 'Failed to stop session');
    } finally {
      void refreshRunningSessions();
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

      {cachedSession && (
        <div className="session-banner" role="status">
          <div>Cached session: {cachedSession.sessionId}</div>
          <div>Game: {gameLookup.get(cachedSession.gameId) ?? cachedSession.gameId}</div>
          <div>Emulator: {cachedSession.emulatorId || '—'}</div>
          <button type="button" onClick={() => handleStopSession(cachedSession.sessionId)} disabled={running || executing}>
            Stop session
          </button>
        </div>
      )}

      <section aria-label="Connect to game">
        <h2>Start a session</h2>
        <p>Select a connect-to-game action and start a session. The returned sessionId will be cached on the service for reuse.</p>

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
            <div>Running session: {selectedRunningSession?.sessionId ?? 'none'}</div>
          </div>
        )}

        <div className="form-actions">
          <button type="button" onClick={handleRun} disabled={running || loadingActions || !selectedAction}>Start session</button>
        </div>
      </section>

      <section aria-label="Running sessions">
        <h2>Running sessions</h2>
        {runningSessionsLoading && <div className="form-hint">Loading running sessions...</div>}
        {!runningSessionsLoading && runningSessions.length === 0 && <div className="form-hint">No running sessions.</div>}
        {!runningSessionsLoading && runningSessions.length > 0 && (
          <ul className="running-sessions-list">
            {runningSessions.map((s) => (
              <li key={s.sessionId} className="running-session-row">
                <div>Session: {s.sessionId}</div>
                <div>Game: {gameLookup.get(s.gameId) ?? s.gameId}</div>
                <div>Emulator: {s.emulatorId || '—'}</div>
                <div className="running-session-status">
                  <span className="run-label">Status:</span>
                  <StatusChip status={s.status} />
                </div>
                <button type="button" onClick={() => handleStopSession(s.sessionId)} disabled={running || executing}>Stop</button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-label="Run details">
        <h2>Run details</h2>
        <RunDetails
          loading={runningSessionsLoading}
          session={primaryRunDetails}
          gameName={primaryRunDetails ? gameLookup.get(primaryRunDetails.gameId) : undefined}
        />
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
            <div>Running session: {commandRunningSession?.sessionId ?? 'none'}</div>
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