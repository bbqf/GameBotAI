import React, { useCallback, useEffect, useState } from 'react';
import { getRunningSessions, RunningSessionDto } from '../../services/sessionsApi';
import { HistoryList } from './HistoryList';
import { QueueBanner } from './QueueBanner';
import { StepList } from './StepList';
import { useStepThrough, UseStepThroughOptions } from './useStepThrough';
import { ValuesForm } from './ValuesForm';
import './StepThroughPanel.css';

type StepThroughPanelProps = UseStepThroughOptions & {
  sequenceId: string;
  sequenceName: string;
  onClose: () => void;
};

const describeSession = (session: RunningSessionDto): string =>
  `${session.gameId} on ${session.emulatorId} (${session.sessionId.slice(0, 8)})`;

/**
 * The step-through view of a saved sequence (feature 127). The author starts it on a game session, runs one
 * step at a time, selects any step as the next step, and reads the history. The author looks at the emulator
 * for the effect of each step. The Sequences page opens this panel for a saved sequence only.
 */
export const StepThroughPanel: React.FC<StepThroughPanelProps> = ({ sequenceId, sequenceName, onClose, ...pollOptions }) => {
  const stepThrough = useStepThrough(pollOptions);
  const { state, error, busy } = stepThrough;
  const [sessions, setSessions] = useState<RunningSessionDto[]>([]);
  const [sessionsError, setSessionsError] = useState<string | undefined>(undefined);
  const [loadingSessions, setLoadingSessions] = useState(false);
  const [selectedSession, setSelectedSession] = useState('');

  const loadSessions = useCallback(async () => {
    setLoadingSessions(true);
    setSessionsError(undefined);
    try {
      const running = await getRunningSessions();
      setSessions(running);
      setSelectedSession((current) => (running.some((s) => s.sessionId === current) ? current : running[0]?.sessionId ?? ''));
    } catch (caught) {
      setSessions([]);
      setSessionsError((caught as Error)?.message ?? 'The game sessions could not be loaded.');
    } finally {
      setLoadingSessions(false);
    }
  }, []);

  useEffect(() => {
    void loadSessions();
  }, [loadSessions]);

  const close = async () => {
    await stepThrough.end();
    onClose();
  };

  const isRunning = state?.state === 'running';
  const isComplete = state?.state === 'complete';
  const controlsLocked = busy || isRunning;

  const errorBox = error ? (
    <div className="form-error step-through-error" role="alert">
      <span>{error.message}</span>
      {error.code === 'sequence_changed' && (
        <button type="button" onClick={() => void stepThrough.restart()} disabled={busy}>Restart</button>
      )}
    </div>
  ) : null;

  if (!state) {
    return (
      <section className="step-through-panel" aria-label="Step through">
        <header className="step-through-header-row">
          <h3>Step through: {sequenceName}</h3>
          <button type="button" onClick={() => void close()}>Close</button>
        </header>
        <p className="form-hint">
          The step-through runs the saved sequence on a game session, one step at a time. Look at the emulator to see the effect of each step.
        </p>
        {errorBox}
        <div className="field">
          <label htmlFor="step-through-session">Game session</label>
          <select
            id="step-through-session"
            value={selectedSession}
            onChange={(e) => setSelectedSession(e.target.value)}
            disabled={loadingSessions || sessions.length === 0}
          >
            {sessions.length === 0 && <option value="">No game session is connected</option>}
            {sessions.map((session) => (
              <option key={session.sessionId} value={session.sessionId}>{describeSession(session)}</option>
            ))}
          </select>
          <button type="button" onClick={() => void loadSessions()} disabled={loadingSessions}>Refresh</button>
        </div>
        {sessionsError && <div className="form-error" role="alert">{sessionsError}</div>}
        {!loadingSessions && !sessionsError && sessions.length === 0 && (
          <div className="form-hint" role="status">Start a game session on the Execution page, then click Refresh.</div>
        )}
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => void stepThrough.start(sequenceId, selectedSession)}
          disabled={busy || !selectedSession}
        >
          Start
        </button>
      </section>
    );
  }

  return (
    <section className="step-through-panel" aria-label="Step through">
      <header className="step-through-header-row">
        <h3>Step through: {state.sequenceName}</h3>
        <span className={`step-through-state step-through-state--${state.state}`} data-testid="step-through-state">
          {state.state === 'complete' ? 'complete' : state.state}
        </span>
        <button type="button" onClick={() => void close()}>Close</button>
      </header>

      {state.queue && <QueueBanner queue={state.queue} busy={busy} onPause={() => void stepThrough.pauseQueue()} />}
      {errorBox}

      <div className="step-through-controls">
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => void stepThrough.runNext()}
          disabled={controlsLocked || !state.cursor}
        >
          Run next step
        </button>
        <button type="button" onClick={() => void stepThrough.cancel()} disabled={!isRunning || busy}>Cancel step</button>
        <button type="button" onClick={() => void stepThrough.restart()} disabled={controlsLocked}>Restart</button>
      </div>

      {isRunning && <div className="form-hint" role="status">A step runs now. Look at the emulator. You can cancel the step.</div>}
      {isComplete && (
        <div className="form-hint" role="status">
          The sequence is complete. Select a step to run it again, or click Restart.
        </div>
      )}

      <StepList
        nodes={state.nodes}
        cursor={state.cursor}
        runningPath={state.running?.path}
        history={state.history}
        disabled={controlsLocked}
        onSelect={(path) => void stepThrough.select(path)}
      />

      <ValuesForm
        parameters={state.parameters}
        outcomes={state.outcomes}
        nodes={state.nodes}
        disabled={controlsLocked}
        onApply={(values) => void stepThrough.setValues(values)}
      />

      <h4>History</h4>
      <HistoryList history={state.history} nodes={state.nodes} />
    </section>
  );
};
