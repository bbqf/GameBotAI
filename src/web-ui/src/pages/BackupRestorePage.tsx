import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  BackupSelection,
  ConflictReport,
  RestoreResult,
  downloadBackup,
  validateRestore,
  applyRestore
} from '../services/backup';
import { listCommands, CommandDto } from '../services/commands';
import { getJson } from '../lib/api';

type SequenceItem = { id: string; name: string };

type RestorePhase =
  | { kind: 'idle' }
  | { kind: 'uploading' }
  | { kind: 'conflict_report'; report: ConflictReport; file: File }
  | { kind: 'applying' }
  | { kind: 'success'; result: RestoreResult }
  | { kind: 'error'; message: string };

export const BackupRestorePage: React.FC = () => {
  const [commands, setCommands] = useState<CommandDto[]>([]);
  const [sequences, setSequences] = useState<SequenceItem[]>([]);
  const [loadingLists, setLoadingLists] = useState(true);
  const [listsError, setListsError] = useState<string | null>(null);

  const [selectedCommandIds, setSelectedCommandIds] = useState<Set<string>>(new Set());
  const [selectedSequenceIds, setSelectedSequenceIds] = useState<Set<string>>(new Set());
  const [backupLoading, setBackupLoading] = useState(false);
  const [backupError, setBackupError] = useState<string | null>(null);

  const [restorePhase, setRestorePhase] = useState<RestorePhase>({ kind: 'idle' });
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        const [cmds, seqs] = await Promise.all([
          listCommands(),
          getJson<SequenceItem[]>('/api/sequences')
        ]);
        if (!cancelled) {
          setCommands(cmds ?? []);
          setSequences((seqs as SequenceItem[]) ?? []);
        }
      } catch (err) {
        if (!cancelled) setListsError(err instanceof Error ? err.message : 'Failed to load lists.');
      } finally {
        if (!cancelled) setLoadingLists(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, []);

  const toggleCommand = (id: string) =>
    setSelectedCommandIds(prev => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const toggleSequence = (id: string) =>
    setSelectedSequenceIds(prev => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const selectAllCommands = () =>
    setSelectedCommandIds(prev =>
      prev.size === commands.length ? new Set() : new Set(commands.map(c => c.id))
    );

  const selectAllSequences = () =>
    setSelectedSequenceIds(prev =>
      prev.size === sequences.length ? new Set() : new Set(sequences.map(s => s.id))
    );

  const nothingSelected = selectedCommandIds.size === 0 && selectedSequenceIds.size === 0;
  const nothingAvailable = commands.length === 0 && sequences.length === 0;

  const handleDownloadBackup = async () => {
    setBackupError(null);
    setBackupLoading(true);
    try {
      const selection: BackupSelection = {
        commandIds: Array.from(selectedCommandIds),
        sequenceIds: Array.from(selectedSequenceIds)
      };
      await downloadBackup(selection);
    } catch (err) {
      setBackupError(err instanceof Error ? err.message : 'Backup download failed. Please try again.');
    } finally {
      setBackupLoading(false);
    }
  };

  const handleFileChange = useCallback(async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setRestorePhase({ kind: 'uploading' });
    try {
      const report = await validateRestore(file);
      setRestorePhase({ kind: 'conflict_report', report, file });
    } catch (err) {
      setRestorePhase({ kind: 'error', message: err instanceof Error ? err.message : 'Failed to validate archive.' });
    }
    if (fileInputRef.current) fileInputRef.current.value = '';
  }, []);

  const handleConfirmRestore = async () => {
    if (restorePhase.kind !== 'conflict_report') return;
    const { file } = restorePhase;
    setRestorePhase({ kind: 'applying' });
    try {
      const result = await applyRestore(file);
      if (result.rolledBack) {
        setRestorePhase({ kind: 'error', message: result.errorMessage ?? 'Restore failed and was rolled back. Please verify your data manually.' });
      } else {
        setRestorePhase({ kind: 'success', result });
      }
    } catch (err) {
      setRestorePhase({ kind: 'error', message: err instanceof Error ? err.message : 'Restore failed. Please try again.' });
    }
  };

  const handleCancelRestore = () => setRestorePhase({ kind: 'idle' });

  return (
    <div className="backup-restore-page">
      <section className="backup-section">
        <h2>Backup</h2>
        {loadingLists && <p>Loading...</p>}
        {listsError && <p className="error">{listsError}</p>}
        {!loadingLists && !listsError && nothingAvailable && (
          <p className="empty-state">No commands or sequences exist yet. Create some content first.</p>
        )}
        {!loadingLists && !listsError && !nothingAvailable && (
          <>
            <div className="selection-groups">
              {commands.length > 0 && (
                <div className="selection-group">
                  <div className="group-header">
                    <strong>Commands</strong>
                    <button type="button" onClick={selectAllCommands}>
                      {selectedCommandIds.size === commands.length ? 'Deselect All' : 'Select All'}
                    </button>
                  </div>
                  {commands.map(c => (
                    <label key={c.id}>
                      <input
                        type="checkbox"
                        checked={selectedCommandIds.has(c.id)}
                        onChange={() => toggleCommand(c.id)}
                      />
                      {c.name}
                    </label>
                  ))}
                </div>
              )}
              {sequences.length > 0 && (
                <div className="selection-group">
                  <div className="group-header">
                    <strong>Sequences</strong>
                    <button type="button" onClick={selectAllSequences}>
                      {selectedSequenceIds.size === sequences.length ? 'Deselect All' : 'Select All'}
                    </button>
                  </div>
                  {sequences.map(s => (
                    <label key={s.id}>
                      <input
                        type="checkbox"
                        checked={selectedSequenceIds.has(s.id)}
                        onChange={() => toggleSequence(s.id)}
                      />
                      {s.name}
                    </label>
                  ))}
                </div>
              )}
            </div>
            <button
              type="button"
              onClick={handleDownloadBackup}
              disabled={nothingSelected || backupLoading}
            >
              {backupLoading ? 'Generating backup…' : 'Download Backup'}
            </button>
            {backupError && <p className="error">{backupError}</p>}
          </>
        )}
      </section>

      <section className="restore-section">
        <h2>Restore</h2>

        {restorePhase.kind === 'idle' && (
          <>
            <p>Select a backup archive (.zip) to restore commands, sequences, and images.</p>
            <label>
              <input
                ref={fileInputRef}
                type="file"
                accept=".zip"
                onChange={handleFileChange}
                style={{ display: 'none' }}
                data-testid="restore-file-input"
              />
              <button type="button" onClick={() => fileInputRef.current?.click()}>
                Upload &amp; Check Archive
              </button>
            </label>
          </>
        )}

        {restorePhase.kind === 'uploading' && <p>Checking archive for conflicts…</p>}

        {restorePhase.kind === 'applying' && <p>Restoring… please wait.</p>}

        {restorePhase.kind === 'conflict_report' && (
          <div className="conflict-dialog" role="dialog" aria-modal="true">
            <h3>Restore Confirmation</h3>
            <p>
              Archive contains {restorePhase.report.totalCommands} command(s),{' '}
              {restorePhase.report.totalSequences} sequence(s), and{' '}
              {restorePhase.report.totalImages} image(s).
            </p>
            {restorePhase.report.hasConflicts && (
              <div className="conflict-details">
                <p className="warning">
                  The following existing objects will be <strong>overwritten</strong>:
                </p>
                {restorePhase.report.conflictingCommandNames.length > 0 && (
                  <>
                    <strong>Commands:</strong>
                    <ul>{restorePhase.report.conflictingCommandNames.map(n => <li key={n}>{n}</li>)}</ul>
                  </>
                )}
                {restorePhase.report.conflictingSequenceNames.length > 0 && (
                  <>
                    <strong>Sequences:</strong>
                    <ul>{restorePhase.report.conflictingSequenceNames.map(n => <li key={n}>{n}</li>)}</ul>
                  </>
                )}
                {restorePhase.report.conflictingImageIds.length > 0 && (
                  <>
                    <strong>Images:</strong>
                    <ul>{restorePhase.report.conflictingImageIds.map(id => <li key={id}>{id}</li>)}</ul>
                  </>
                )}
              </div>
            )}
            {!restorePhase.report.hasConflicts && <p>No conflicts detected.</p>}
            <div className="dialog-actions">
              <button type="button" onClick={handleCancelRestore}>Cancel</button>
              <button type="button" onClick={handleConfirmRestore}>
                {restorePhase.report.hasConflicts ? 'Confirm Overwrite' : 'Confirm Restore'}
              </button>
            </div>
          </div>
        )}

        {restorePhase.kind === 'success' && (
          <div className="restore-success">
            <p>
              Restore complete: {restorePhase.result.restoredCommands} command(s),{' '}
              {restorePhase.result.restoredSequences} sequence(s),{' '}
              {restorePhase.result.restoredImages} image(s) restored.
            </p>
            <button type="button" onClick={handleCancelRestore}>Restore Another</button>
          </div>
        )}

        {restorePhase.kind === 'error' && (
          <div className="restore-error">
            <p className="error">{restorePhase.message}</p>
            <button type="button" onClick={handleCancelRestore}>Try Again</button>
          </div>
        )}
      </section>
    </div>
  );
};
