import React, { useCallback, useEffect, useRef, useState } from 'react';
import { QueueDetailDto, QueueEntryDto, setQueueTemplateLink } from '../../services/queues';
import { SequenceDto } from '../../services/sequences';
import {
  getQueueTemplate,
  listQueueTemplates,
  QueueTemplateDetail,
  QueueTemplateSummary,
  saveQueueTemplate,
} from '../../services/queueTemplates';
import { EntrySchedule } from './QueueEntryList';
import { QueueSchedulingAreas } from './QueueSchedulingAreas';
import { QueueTemplateControls } from './QueueTemplateControls';
import { SchedulingAreasState } from './schedulingAreas';
import type { ParameterBinding } from '../parameters/types';
import { buildTemplateSaveEntries, defaultEntrySchedule, scheduleFromTemplateEntry } from './templateEntryEditing';

export const NO_TEMPLATE_MESSAGE = 'Link a template to edit this queue while it runs.';

export type RunningQueueEditorProps = {
  queue: Pick<QueueDetailDto, 'id' | 'linkedTemplateId' | 'linkedTemplateName' | 'entries'>;
  sequences: SequenceDto[];
  /**
   * Edits that the page already holds, for a queue that started while the operator edited it. The editor starts from them
   * and does not read the template.
   */
  carried?: { entries: QueueEntryDto[]; schedule: Record<string, EntrySchedule> };
  /** Called after each successful template save, so the page can read the waiting changes again. */
  onTemplateSaved?: () => void;
  /** Called after a template is linked to a queue that had none. */
  onLinked?: () => void;
};

type SaveResult = { kind: 'success' | 'error'; message: string };

const messageOf = (err: unknown, fallback: string): string => (err instanceof Error && err.message ? err.message : fallback);

/**
 * The editor of a running queue (feature 173). It reads and changes the linked template only. It never calls the
 * add-entry, remove-entry, or replace-entries calls of the queue, so the run does not change until the operator pauses,
 * reviews, and applies the changes.
 */
export const RunningQueueEditor: React.FC<RunningQueueEditorProps> = ({ queue, sequences, carried, onTemplateSaved, onLinked }) => {
  const templateId = queue.linkedTemplateId;
  const [entries, setEntries] = useState<QueueEntryDto[]>(carried?.entries ?? []);
  const [schedule, setSchedule] = useState<Record<string, EntrySchedule>>(carried?.schedule ?? {});
  const [templateName, setTemplateName] = useState<string | undefined>(queue.linkedTemplateName ?? undefined);
  const [loadError, setLoadError] = useState<string | undefined>(undefined);
  const [saveResult, setSaveResult] = useState<SaveResult | undefined>(undefined);
  const newRowCounter = useRef(0);
  const carriedRef = useRef(carried);

  const showTemplate = useCallback((template: QueueTemplateDetail) => {
    const rows = template.entries.map((entry, index) => ({ entry, id: entry.entryId ?? `row-${index}` }));
    setEntries(rows.map(({ entry, id }) => ({ entryId: id, sequenceId: entry.sequenceId, sequenceName: entry.sequenceName, stale: entry.stale })));
    setSchedule(Object.fromEntries(rows.map(({ entry, id }) => [id, scheduleFromTemplateEntry(entry)])));
    setTemplateName(template.name);
  }, []);

  useEffect(() => {
    if (!templateId || carriedRef.current) return;
    let cancelled = false;
    getQueueTemplate(templateId)
      .then((template) => { if (!cancelled) { showTemplate(template); setLoadError(undefined); } })
      .catch((err: unknown) => { if (!cancelled) setLoadError(messageOf(err, 'Failed to load the template')); });
    return () => { cancelled = true; };
  }, [templateId, showTemplate]);

  const patchSchedule = (entryId: string, patch: Partial<EntrySchedule>) =>
    setSchedule((prev) => ({ ...prev, [entryId]: { ...(prev[entryId] ?? defaultEntrySchedule()), ...patch } }));

  const onAdd = (sequenceId: string) => {
    const id = `new-${newRowCounter.current++}`;
    const name = sequences.find((s) => s.id === sequenceId)?.name ?? null;
    setEntries((prev) => [...prev, { entryId: id, sequenceId, sequenceName: name, stale: false }]);
    setSchedule((prev) => ({ ...prev, [id]: defaultEntrySchedule() }));
  };

  const onRemove = (entryId: string) => {
    setEntries((prev) => prev.filter((e) => e.entryId !== entryId));
    setSchedule((prev) => {
      const next = { ...prev };
      delete next[entryId];
      return next;
    });
  };

  const onReorderAndReassign = (next: SchedulingAreasState) => {
    setSchedule(next.schedule);
    setEntries((prev) => {
      const byId = new Map(prev.map((e) => [e.entryId, e]));
      return next.orderedEntryIds.map((id) => byId.get(id)).filter((e): e is QueueEntryDto => Boolean(e));
    });
  };

  const handleSaveTemplate = async (name: string) => {
    setSaveResult(undefined);
    const rows = entries.map((e) => ({ sequenceId: e.sequenceId, schedule: schedule[e.entryId] }));
    const saved = await saveQueueTemplate({ name, entries: buildTemplateSaveEntries(rows), overwrite: true });
    if (saved?.id && saved.id !== templateId) await setQueueTemplateLink(queue.id, saved.id);
    // The save gives the IDs of the new entries. Start from them, so the next save keeps their identity.
    if (Array.isArray(saved?.entries) && saved.entries.length === entries.length) showTemplate(saved);
    setSaveResult({ kind: 'success', message: `Template "${name}" saved successfully.` });
    onTemplateSaved?.();
  };

  if (!templateId) {
    return <NoTemplateView queue={queue} onLinked={onLinked} />;
  }

  return (
    <QueueTemplateControls
      templateOnly
      associatedTemplateName={templateName}
      associatedTemplateId={templateId}
      status="Running"
      onSaveTemplate={handleSaveTemplate}
      onLoadTemplate={() => undefined}
      onReload={() => undefined}
      saveResult={saveResult}
    >
      {loadError && <div className="form-error" role="alert">{loadError}</div>}
      <QueueSchedulingAreas
        entries={entries}
        sequences={sequences}
        entrySchedule={schedule}
        onAdd={onAdd}
        onRemove={onRemove}
        onReorderAndReassign={onReorderAndReassign}
        onTimerTimeChange={(id, t) => patchSchedule(id, { timerTimeOfDay: t })}
        onTimerModeChange={(id, mode) => patchSchedule(id, { timerMode: mode })}
        onTimerRelativeOffsetChange={(id, offset) => patchSchedule(id, { timerMode: 'relative', timerRelativeOffset: offset })}
        onToggleEnabled={(id, enabled) => patchSchedule(id, { enabled })}
        onParameterValuesChange={(id: string, values: ParameterBinding[]) => patchSchedule(id, { parameterValues: values })}
      />
    </QueueTemplateControls>
  );
};

type NoTemplateViewProps = Pick<RunningQueueEditorProps, 'queue' | 'onLinked'>;

/** A running queue with no linked template: the entries are read-only, and the operator can link a template. */
const NoTemplateView: React.FC<NoTemplateViewProps> = ({ queue, onLinked }) => {
  const [templates, setTemplates] = useState<QueueTemplateSummary[]>([]);
  const [chosen, setChosen] = useState('');
  const [linking, setLinking] = useState(false);
  const [error, setError] = useState<string | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;
    listQueueTemplates()
      .then((list) => { if (!cancelled) setTemplates(list ?? []); })
      .catch(() => { if (!cancelled) setTemplates([]); });
    return () => { cancelled = true; };
  }, []);

  const link = async () => {
    if (!chosen) return;
    setLinking(true);
    setError(undefined);
    try {
      await setQueueTemplateLink(queue.id, chosen);
      onLinked?.();
    } catch (err: unknown) {
      setError(messageOf(err, 'Failed to link the template'));
    } finally {
      setLinking(false);
    }
  };

  return (
    <section className="queue-template-controls" aria-label="Queue templates">
      <p className="form-hint" role="status">{NO_TEMPLATE_MESSAGE}</p>
      <ul className="queue-entries-readonly" aria-label="Queue sequences (read only)">
        {queue.entries.map((entry) => (
          <li key={entry.entryId}>{entry.sequenceName ?? entry.sequenceId}</li>
        ))}
      </ul>
      <div className="queue-template-row">
        <label htmlFor="running-link-template">Template to link</label>
        <select id="running-link-template" value={chosen} onChange={(e) => setChosen(e.target.value)} disabled={linking}>
          <option value="">Select a template…</option>
          {templates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
        </select>
        <button type="button" onClick={() => void link()} disabled={!chosen || linking}>Link template</button>
      </div>
      {error && <div className="form-error" role="alert">{error}</div>}
    </section>
  );
};
