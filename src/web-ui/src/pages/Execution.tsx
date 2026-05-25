import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useGames } from '../services/useGames';
import { useAdbDevices } from '../services/useAdbDevices';
import { getRunningSessions, startSession, stopSession, RunningSessionDto } from '../services/sessionsApi';
import { listCommands, forceExecuteCommand, CommandDto } from '../services/commands';
import { listSequences, executeSequence, SequenceDto } from '../services/sequences';
import { ApiError } from '../lib/api';
import { StatusChip } from '../features/execution/StatusChip';
import { RunDetails } from '../features/execution/RunDetails';

export const EXECUTION_AREA_PATH = '/execution';
export const EXECUTION_LOGS_AREA_PATH = '/execution-logs';

export function formatCaptureRate(fps?: number | null): string {
  if (fps == null || fps <= 0) return '—';
  if (fps >= 1) return `${fps.toFixed(1)} FPS`;
  return `${(1 / fps).toFixed(1)} s/frame`;
}

export const ExecutionPage: React.FC = () => {
  const { data: gamesData, error: gamesError } = useGames();
  const { devices, loading: loadingDevices, error: devicesError } = useAdbDevices(true);
  const [commands, setCommands] = useState<CommandDto[]>([]);
  const [loadingCommands, setLoadingCommands] = useState(true);
  const [selectedGameId, setSelectedGameId] = useState<string>('');
  const [selectedAdbSerial, setSelectedAdbSerial] = useState<string>('');
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
  const [sequences, setSequences] = useState<SequenceDto[]>([]);
  const [loadingSequences, setLoadingSequences] = useState(true);
  const [selectedSequenceId, setSelectedSequenceId] = useState<string | undefined>(undefined);
  const [sequenceMessage, setSequenceMessage] = useState<string | undefined>(undefined);
  const [sequenceError, setSequenceError] = useState<string | undefined>(undefined);
  const [executingSequence, setExecutingSequence] = useState(false);

  const gameLookup = useMemo(() => {
    const map = new Map<string, string>();
    (gamesData ?? []).forEach((g) => map.set(g.id, g.name));
    return map;
  }, [gamesData]);

  const selectedDevice = useMemo(
    () => devices.find((d) => d.serial === selectedAdbSerial),
    [devices, selectedAdbSerial]
  );

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
    const load = async () => {
      setLoadingSequences(true);
      try {
        const data = await listSequences();
        if (Array.isArray(data)) {
          setSequences(data);
        } else {
          setSequences([]);
        }
      } catch (err: any) {
        setSequences([]);
      } finally {
        setLoadingSequences(false);
      }
    };
    void load();
  }, []);

  useEffect(() => {
    if (!selectedGameId && (gamesData?.length ?? 0) > 0) {
      setSelectedGameId(gamesData![0].id);
    }
  }, [gamesData, selectedGameId]);

  useEffect(() => {
    if (!selectedAdbSerial && devices.length > 0) {
      setSelectedAdbSerial(devices[0].serial);
    }
  }, [devices, selectedAdbSerial]);

  useEffect(() => {
    if (!selectedCommandId && commands.length > 0) {
      setSelectedCommandId(commands[0].id);
    }
  }, [commands, selectedCommandId]);

  useEffect(() => {
    if (!selectedSequenceId && sequences.length > 0) {
      setSelectedSequenceId(sequences[0].id);
    }
  }, [sequences, selectedSequenceId]);

  const findCommandCacheMeta = (commandId?: string) => {
    if (!commandId) return undefined;
    const cmd = commands.find((c) => c.id === commandId);
    if (!cmd || !cmd.steps || cmd.steps.length === 0) return undefined;
    const firstActionStep = [...cmd.steps].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).find((s) => s.type === 'Action');
    if (!firstActionStep) return undefined;
    // Legacy action-step metadata is no longer available after action cutover.
    return undefined;
  };

  useEffect(() => {
    const meta = findCommandCacheMeta(selectedCommandId);
    setCommandCacheMeta(meta);
  }, [selectedCommandId, commands]);

  const selectedRunningSession = useMemo(() => {
    if (!selectedGameId || !selectedAdbSerial) return undefined;
    return runningSessions.find((s) => s.gameId === selectedGameId && s.emulatorId === selectedAdbSerial);
  }, [selectedGameId, selectedAdbSerial, runningSessions]);

  const commandRunningSession = useMemo(() => {
    if (!commandCacheMeta) return undefined;
    return runningSessions.find((s) => s.gameId === commandCacheMeta.gameId && s.emulatorId === commandCacheMeta.adbSerial);
  }, [commandCacheMeta, runningSessions]);

  const cachedSession = commandRunningSession ?? selectedRunningSession ?? runningSessions[0];

  const primaryRunDetails = commandRunningSession ?? selectedRunningSession ?? runningSessions[0];

  const handleRun = async () => {
    const adbSerial = selectedAdbSerial;
    const gameId = selectedGameId;
    if (!gameId || !adbSerial) {
      setError('Select both game and adb serial before starting a session.');
      return;
    }

    setRunning(true);
    setMessage(undefined);
    setError(undefined);

    try {
      const response = await startSession({ gameId, adbSerial });
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
      if (meta) {
        const runningSession = runningSessions.find((s) => s.gameId === meta.gameId && s.emulatorId === meta.adbSerial && `${s.status}`.toLowerCase() === 'running');
        if (!runningSession) {
          setExecuting(false);
          setCommandError(`No running session found for ${meta.gameId}/${meta.adbSerial}. Start a session first or enter a sessionId.`);
          return;
        }
        resolvedSessionId = runningSession.sessionId;
      } else if (cachedSession) {
        resolvedSessionId = cachedSession.sessionId;
      } else {
        setExecuting(false);
        setCommandError('No running session available. Start a session first or enter a sessionId.');
        return;
      }
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

  const handleExecuteSequence = async () => {
    if (!selectedSequenceId) {
      setSequenceError('Select a sequence to execute.');
      return;
    }

    setExecutingSequence(true);
    setSequenceMessage(undefined);
    setSequenceError(undefined);

    try {
      await executeSequence(selectedSequenceId, cachedSession?.sessionId);
      setSequenceMessage('Sequence executed successfully.');
    } catch (err: any) {
      if (err instanceof ApiError) {
        setSequenceError(err.message);
      } else {
        setSequenceError(err?.message ?? 'Failed to execute sequence');
      }
    } finally {
      setExecutingSequence(false);
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
      {devicesError && <div className="form-error" role="alert">{devicesError}</div>}
      {error && <div className="form-error" role="alert">{error}</div>}
      {message && <div className="form-hint" role="status">{message}</div>}
      {commandError && <div className="form-error" role="alert">{commandError}</div>}
      {commandMessage && <div className="form-hint" role="status">{commandMessage}</div>}
      {sequenceError && <div className="form-error" role="alert">{sequenceError}</div>}
      {sequenceMessage && <div className="form-hint" role="status">{sequenceMessage}</div>}

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
        <p>Select game and adb serial, then start a session. The returned sessionId will be cached on the service for reuse.</p>

        <div className="field">
          <label htmlFor="connect-game-select">Game</label>
          <select
            id="connect-game-select"
            aria-label="Game"
            value={selectedGameId}
            onChange={(e) => setSelectedGameId(e.target.value)}
            disabled={running || (gamesData?.length ?? 0) === 0}
          >
            {(gamesData ?? []).map((g) => (
              <option key={g.id} value={g.id}>{g.name}</option>
            ))}
          </select>
          {(gamesData?.length ?? 0) === 0 && <div className="form-hint">No games available.</div>}
        </div>

        <div className="field">
          <label htmlFor="connect-adb-select">ADB serial</label>
          <select
            id="connect-adb-select"
            aria-label="ADB serial"
            value={selectedAdbSerial}
            onChange={(e) => setSelectedAdbSerial(e.target.value)}
            disabled={running || loadingDevices || devices.length === 0}
          >
            {devices.map((d) => (
              <option key={d.serial} value={d.serial}>{d.serial}{d.state ? ` (${d.state})` : ''}</option>
            ))}
          </select>
          {loadingDevices && <div className="form-hint">Loading adb devices...</div>}
          {!loadingDevices && devices.length === 0 && <div className="form-hint">No adb devices detected.</div>}
        </div>

        <div className="action-details">
          <div>Game: {(gameLookup.get(selectedGameId) ?? selectedGameId) || '—'}</div>
          <div>ADB Serial: {(selectedDevice?.serial ?? selectedAdbSerial) || '—'}</div>
          <div>Running session: {selectedRunningSession?.sessionId ?? 'none'}</div>
        </div>

        <div className="form-actions">
          <button type="button" onClick={handleRun} disabled={running || !selectedGameId || !selectedAdbSerial}>Start session</button>
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
                {s.captureRateFps != null && s.captureRateFps > 0 && (
                  <div className="running-session-capture-rate">
                    <span className="run-label">Screen capture:</span> {formatCaptureRate(s.captureRateFps)}
                  </div>
                )}
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

      <section aria-label="Execute sequence">
        <h2>Execute a sequence</h2>
        <p>Run a sequence directly. The sequence runner will execute all steps in order.</p>

        <div className="field">
          <label htmlFor="sequence-select">Sequence</label>
          <select
            id="sequence-select"
            aria-label="Sequence"
            value={selectedSequenceId ?? ''}
            onChange={(e) => { setSelectedSequenceId(e.target.value); setSequenceMessage(undefined); setSequenceError(undefined); }}
            disabled={loadingSequences || executingSequence || sequences.length === 0}
          >
            {sequences.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
          {loadingSequences && <div className="form-hint">Loading sequences...</div>}
          {!loadingSequences && sequences.length === 0 && <div className="form-hint">No sequences available.</div>}
        </div>

        <div className="form-actions">
          <button type="button" onClick={handleExecuteSequence} disabled={executingSequence || loadingSequences || sequences.length === 0}>Execute sequence</button>
        </div>
      </section>
    </div>
  );
};