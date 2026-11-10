import React, { useEffect, useRef, useState } from 'react';
import { ApiError } from '../../lib/api';
import { setTemplateEntryParameters } from '../../lib/queueEntryParameters';
import { setLeaveGuard, shouldBlockLeave } from '../../lib/phoneLeaveGuard';
import { saveQueueTemplate, QueueTemplateDetail } from '../../services/queueTemplates';
import { getSequence } from '../../services/sequences';
import type { QueueDetailDto } from '../../services/queues';
import type { ConnectionState } from '../../hooks/useConnectionState';
import { ParameterBindingForm } from '../parameters/ParameterBindingForm';
import type { ParameterBinding, ParameterDeclaration } from '../parameters/types';
import { scheduleSummary } from './scheduleSummary';
import './PhoneEntryPage.css';

export type PhoneEntryPageProps = {
  detail: QueueDetailDto;
  /** The template of the queue. Null when no template is linked or it did not load. */
  template: QueueTemplateDetail | null;
  entryId: string;
  connectionState?: ConnectionState;
  /**
   * A counter that the app raises when the phone back step met the leave guard. A new value opens the
   * keep-or-discard dialog (rule E-08).
   */
  leaveRequest?: number;
  /** Go back to the queue detail. */
  onBack: () => void;
  /** Called with the template that the server returned after a save. */
  onSaved: (template: QueueTemplateDetail) => void;
};

type DeclarationsState = 'loading' | 'loaded' | 'failed';

const errorText = (err: unknown): string =>
  err instanceof ApiError || err instanceof Error ? err.message : 'The request failed.';

/** A stable form of a binding list, so that the page can tell whether the draft differs from the saved values. */
const normalize = (bindings: ParameterBinding[]): string =>
  JSON.stringify(bindings.map((b) => ({ name: b.name, value: b.value ?? null, values: b.values ?? null })));

const sameBindings = (a: ParameterBinding[], b: ParameterBinding[]): boolean => normalize(a) === normalize(b);

/**
 * The entry page of the phone (feature 185). It shows the schedule of one template entry as text and the shared form
 * for the parameter values of the entry. One Save button writes the values to the linked template.
 */
export const PhoneEntryPage: React.FC<PhoneEntryPageProps> = ({
  detail,
  template,
  entryId,
  connectionState = 'online',
  leaveRequest = 0,
  onBack,
  onSaved
}) => {
  const entryIndex = template ? template.entries.findIndex((e) => e.entryId === entryId) : -1;
  const entry = template && entryIndex >= 0 ? template.entries[entryIndex] : null;
  const sequenceId = entry?.sequenceId ?? null;

  const [declarations, setDeclarations] = useState<ParameterDeclaration[]>([]);
  const [declarationsState, setDeclarationsState] = useState<DeclarationsState>('loading');
  // null = the operator changed nothing. The page then shows the saved values, so a new template read can replace them.
  const [draft, setDraft] = useState<ParameterBinding[] | null>(null);
  const [adHocShown, setAdHocShown] = useState(false);
  const [saving, setSaving] = useState(false);
  const [savedNote, setSavedNote] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const savingRef = useRef(false);
  const [leavePrompt, setLeavePrompt] = useState(false);
  const lastLeaveRequest = useRef(leaveRequest);

  // One read of the sequence gives the declarations.
  useEffect(() => {
    if (!sequenceId) return undefined;
    let cancelled = false;
    setDeclarationsState('loading');
    getSequence(sequenceId)
      .then((sequence) => {
        if (cancelled) return;
        setDeclarations(sequence.parameters ?? []);
        setDeclarationsState('loaded');
      })
      .catch(() => {
        if (!cancelled) setDeclarationsState('failed');
      });
    return () => { cancelled = true; };
  }, [sequenceId]);

  // The page has unsaved edits when the draft differs from the saved values.
  const unsaved = draft !== null && !!entry && !sameBindings(draft, entry.parameterValues ?? []);

  // While there are unsaved edits, the app asks this guard before it leaves the page (rule E-08).
  useEffect(() => {
    if (!unsaved) return undefined;
    setLeaveGuard(() => true);
    return () => setLeaveGuard(null);
  }, [unsaved]);

  // The phone back step met the guard. The app raised the counter, so the page asks the operator.
  useEffect(() => {
    if (leaveRequest === lastLeaveRequest.current) return;
    lastLeaveRequest.current = leaveRequest;
    if (unsaved) setLeavePrompt(true);
  }, [leaveRequest, unsaved]);

  if (!entry || !template) {
    return (
      <section className="phone-entry-page" data-testid="phone-entry-page">
        <p role="alert">Entry not found</p>
        <button type="button" className="phone-entry-page__back" onClick={onBack}>← Back</button>
      </section>
    );
  }

  const saved = entry.parameterValues ?? [];
  const values = draft ?? saved;
  const dirty = draft !== null && !sameBindings(draft, saved);
  const lost = connectionState === 'lost';
  const name = entry.sequenceName ?? entry.sequenceId;
  const showForm = declarations.length > 0 || values.length > 0 || adHocShown;
  const canSave = dirty && !saving && !lost && declarationsState === 'loaded';

  const change = (next: ParameterBinding[]) => {
    setDraft(sameBindings(next, saved) ? null : next);
    setSavedNote(false);
    setSaveError(null);
  };

  /** The on-screen Back asks the guard first, like the phone back step does. */
  const requestBack = () => {
    if (shouldBlockLeave()) setLeavePrompt(true);
    else onBack();
  };

  const discard = () => {
    // Clear the guard at once, so that the back step that follows is not blocked again.
    setLeaveGuard(null);
    setLeavePrompt(false);
    setDraft(null);
    onBack();
  };

  const save = async () => {
    if (!canSave || savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    setSavedNote(false);
    setSaveError(null);
    try {
      const result = await saveQueueTemplate(setTemplateEntryParameters(template, entryIndex, values));
      setDraft(null);
      setSavedNote(true);
      onSaved(result);
    } catch (err) {
      // The typed values stay. The page does not send the call again.
      setSaveError(errorText(err));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <section className="phone-entry-page" data-testid="phone-entry-page">
      <button type="button" className="phone-entry-page__back" onClick={requestBack}>← Back</button>
      {leavePrompt && (
        // The dialog is near the top, so that it is in view even when the form is long.
        <div className="phone-entry-page__dialog" role="alertdialog" aria-label="Unsaved changes">
          <p>You have changes that are not saved.</p>
          <button type="button" onClick={() => setLeavePrompt(false)}>Keep editing</button>
          <button type="button" onClick={discard}>Discard changes</button>
        </div>
      )}
      <h2 className="phone-entry-page__name">{name}</h2>
      <p className="phone-entry-page__queue">{detail.name}</p>

      <div className="phone-entry-page__schedule" aria-label="Schedule">
        <span className="phone-entry-page__label">Schedule: </span>
        <span data-testid="entry-schedule">{scheduleSummary(entry)}</span>
        {entry.enabled === false && <span className="phone-entry-page__off"> (off)</span>}
      </div>
      <p className="form-hint">Use a larger screen to edit schedules.</p>

      {declarationsState === 'loading' && <p role="status">Loading…</p>}

      {declarationsState === 'failed' && (
        <div className="phone-entry-page__failed">
          <p role="alert">The parameters did not load.</p>
          {saved.length > 0 && (
            <ul className="phone-entry-page__stored" aria-label="Stored values">
              {saved.map((b) => (
                <li key={b.name}>{b.name}: {b.value ?? 'default'}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      {declarationsState === 'loaded' && (
        <>
          {showForm ? (
            <ParameterBindingForm
              declarations={declarations}
              bindings={values}
              effective={entry.effectiveParameters ?? undefined}
              allowAdHoc
              disabled={saving}
              onChange={change}
            />
          ) : (
            <div className="phone-entry-page__empty">
              <p>This sequence has no parameters.</p>
              <button type="button" onClick={() => setAdHocShown(true)}>Add a value</button>
            </div>
          )}
        </>
      )}

      {savedNote && <p role="status" className="phone-entry-page__saved">Saved.</p>}
      {saveError && <p role="alert" className="phone-entry-page__error">{saveError}</p>}

      <button
        type="button"
        className="phone-entry-page__save"
        disabled={!canSave}
        onClick={() => void save()}
      >
        Save
      </button>
    </section>
  );
};
