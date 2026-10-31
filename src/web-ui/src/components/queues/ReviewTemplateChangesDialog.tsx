import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  apiErrorCode,
  apiErrorHint,
  applyTemplate,
  getTemplateDiff,
  QueueTemplateDiffDto
} from '../../services/queues';
import { TemplateUpgradePanel } from './TemplateUpgradePanel';
import './ReviewTemplateChangesDialog.css';

export type ReviewTemplateChangesDialogProps = {
  queueId: string;
  /** True while the queue is "paused (between sequences)". Apply works only then. */
  paused: boolean;
  onClose: () => void;
  /** Called after the differences are in the queue, so that the parent can read its data again. */
  onApplied?: () => void;
};

type Problem = { message: string; hint: string | null; code: string | null };

const toProblem = (err: unknown, fallback: string): Problem => {
  const payload = (err as { payload?: { error?: { message?: unknown } } } | null)?.payload;
  const serverMessage = payload?.error?.message;
  const message = typeof serverMessage === 'string' && serverMessage.length > 0
    ? serverMessage
    : err instanceof Error && err.message ? err.message : fallback;
  return { message, hint: apiErrorHint(err), code: apiErrorCode(err) };
};

const countText = (n: number): string => `${n} ${n === 1 ? 'change' : 'changes'}`;

/** What the review compares, what Apply does, and that the queue must be paused first (feature 173, FR-014). */
const REVIEW_EXPLANATION =
  'Compare the running queue with its template, then pause the queue and apply the changes that you select.';

/**
 * Review of the differences between a running queue and its linked template (feature 171). The operator selects the
 * changes to apply and applies them to a paused queue. All selected changes go in, or none. A schedule change is not
 * selected at first: a schedule changes only on a direct instruction. The component reads the differences when it
 * opens, and it sends back the token of that read, so a template that changed meanwhile is refused.
 */
export const ReviewTemplateChangesDialog: React.FC<ReviewTemplateChangesDialogProps> = ({ queueId, paused, onClose, onApplied }) => {
  const [diff, setDiff] = useState<QueueTemplateDiffDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [applying, setApplying] = useState(false);
  const [appliedCount, setAppliedCount] = useState<number | null>(null);
  const applyingRef = useRef(false);

  const load = useCallback(async () => {
    setLoading(true);
    setProblem(null);
    setAppliedCount(null);
    try {
      const next = await getTemplateDiff(queueId);
      setDiff(next);
      setSelected(new Set(next.differences.filter((d) => d.selectedByDefault).map((d) => d.key)));
    } catch (err) {
      setDiff(null);
      setProblem(toProblem(err, 'The differences could not be read.'));
    } finally {
      setLoading(false);
    }
  }, [queueId]);

  useEffect(() => {
    void load();
  }, [load]);

  const toggle = (key: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const apply = async () => {
    if (!diff || applyingRef.current || selected.size === 0) return;
    applyingRef.current = true;
    setApplying(true);
    setProblem(null);
    try {
      const keys = diff.differences.filter((d) => selected.has(d.key)).map((d) => d.key);
      const result = await applyTemplate(queueId, diff.baseToken, keys);
      setAppliedCount(result.applied.length);
      onApplied?.();
    } catch (err) {
      setProblem(toProblem(err, 'The changes could not be applied.'));
    } finally {
      applyingRef.current = false;
      setApplying(false);
    }
  };

  const total = diff?.differences.length ?? 0;
  const canApply = paused && selected.size > 0 && !applying && appliedCount === null;

  return (
    <section className="review-template-changes queue-template-section" aria-label="Review template changes" data-testid="review-template-changes">
      <h4>Review template changes</h4>
      {loading && <p className="form-hint" role="status">Loading…</p>}
      {problem && (
        <div className="form-error" role="alert">
          <p>{problem.message}</p>
          {problem.hint && <p>{problem.hint}</p>}
          {problem.code === 'template_changed' && (
            <button type="button" onClick={() => void load()}>Review again</button>
          )}
        </div>
      )}
      {!loading && diff?.needsUpgrade && (
        <>
          <p className="form-hint" role="status" data-testid="review-needs-upgrade">
            This template has entries with no entry ID. Upgrade the template first, then review the changes again.
          </p>
          <TemplateUpgradePanel templateId={diff.templateId} onDone={() => void load()} />
        </>
      )}
      {!loading && diff && !diff.needsUpgrade && total === 0 && appliedCount === null && (
        <p className="form-hint" role="status" data-testid="review-nothing">
          The queue matches its template. There is nothing to apply.
        </p>
      )}
      {!loading && diff && !diff.needsUpgrade && total > 0 && appliedCount === null && (
        <>
          <p className="form-hint" data-testid="review-explanation">{REVIEW_EXPLANATION}</p>
          {!paused && (
            <p className="form-hint" role="status" data-testid="review-not-paused">
              The queue is not paused. Pause the queue first, then apply the changes.
            </p>
          )}
          <ul className="review-template-changes__list">
            {diff.differences.map((d) => {
              const id = `review-${d.key}`;
              return (
                <li key={d.key} className="review-template-changes__row" data-testid="template-difference">
                  <label htmlFor={id}>
                    <input id={id} type="checkbox" checked={selected.has(d.key)} onChange={() => toggle(d.key)} />
                    <span>{d.summary}</span>
                  </label>
                  {d.type === 'schedule' && (
                    <span className="review-template-changes__note" data-testid="schedule-change-note">
                      Schedule change. It is not selected unless you select it.
                    </span>
                  )}
                  {d.type !== 'schedule' && d.affectsSchedule && (
                    <span className="review-template-changes__note">The entry starts as the template states.</span>
                  )}
                </li>
              );
            })}
          </ul>
          <p className="form-hint" role="status" data-testid="review-selected-count">
            {selected.size} of {total} selected
          </p>
        </>
      )}
      {appliedCount !== null && (
        <p className="form-hint" role="status" data-testid="review-done">
          {countText(appliedCount)} applied. Resume the queue to go on with the same run.
        </p>
      )}
      <div className="review-template-changes__actions">
        {!loading && diff && !diff.needsUpgrade && total > 0 && appliedCount === null && (
          <button type="button" className="btn btn-primary" disabled={!canApply} onClick={() => void apply()}>
            {selected.size > 0 ? `Apply ${countText(selected.size)}` : 'Apply'}
          </button>
        )}
        <button type="button" className="btn btn-secondary" onClick={onClose}>Close</button>
      </div>
    </section>
  );
};
