import React, { useState } from 'react';
import type { SequencePrimitiveActionPayload } from '../../types/sequenceFlow';
import type { ParameterScopeEntry } from '../parameters/types';
import { ParameterizableField } from '../parameters/ParameterizableField';

/**
 * How one payload slot is edited. Decided once from the value the sequence was loaded with, not
 * re-derived on every keystroke: replacing `569` with `{{sectionRowY}}` turns the stored value into a
 * string, and without a remembered kind the field would silently stop behaving as a numeric one.
 */
type FieldKind = 'number' | 'text' | 'boolean' | 'complex';

const kindOf = (value: unknown): FieldKind => {
  if (typeof value === 'number') return 'number';
  if (typeof value === 'boolean') return 'boolean';
  if (typeof value === 'string') return 'text';
  return 'complex';
};

const NUMERIC_LITERAL = /^-?\d+(?:\.\d+)?$/;

/**
 * Puts an edited slot back into the payload.
 *
 * A slot that started numeric stays numeric while it holds a plain number, so a field the operator
 * never touched serializes byte-identically to what was loaded. Anything else — notably a
 * `{{name}}` reference — is stored as a string, which is exactly what the runner expects: every
 * consumer parses numeric payload slots defensively, so a reference in a numeric slot resolves.
 */
const coerce = (raw: string, kind: FieldKind): string | number =>
  kind === 'number' && NUMERIC_LITERAL.test(raw.trim()) ? Number(raw.trim()) : raw;

/** One-line description of an inline action, used as the step's collapsed label. */
export const summarizePrimitiveAction = (action: SequencePrimitiveActionPayload): string => {
  const scalars = Object.entries(action.payload ?? {})
    .filter(([, value]) => kindOf(value) !== 'complex')
    .map(([name, value]) => `${name}: ${String(value)}`);
  return scalars.length > 0 ? `${action.type} (${scalars.join(', ')})` : action.type;
};

export type PrimitiveActionFieldsProps = {
  action: SequencePrimitiveActionPayload;
  onChange: (action: SequencePrimitiveActionPayload) => void;
  /** Names offerable by the `{ }` picker — the sequence's declarations plus the queue built-ins. */
  scope: ParameterScopeEntry[];
  disabled?: boolean;
  /** Disambiguates input ids when several of these render on one page. */
  idPrefix: string;
};

/**
 * Editor for a sequence step that dispatches an action inline rather than invoking a command
 * (feature 078).
 *
 * Before this existed the editor understood only `command`, `WaitForImage` and `reschedule-self`
 * steps; every other action type fell through to the command branch and was rewritten on save into a
 * command step pointing at an id that had never existed. That made a sequence like "PNS Pit Ensure
 * Mining" — six `tap` steps and two `reschedule-self` steps inside if-branches — unsafe to open here
 * at all. Rendering the action's own payload keeps such steps intact and, because each scalar slot is
 * a {@link ParameterizableField}, lets a hard-coded coordinate become a parameter reference without
 * anyone hand-editing JSON.
 *
 * Slots holding an object or array (an OCR region, say) are preserved verbatim and shown read-only:
 * they round-trip untouched, which matters more than editing them here.
 */
export const PrimitiveActionFields: React.FC<PrimitiveActionFieldsProps> = ({
  action, onChange, scope, disabled, idPrefix,
}) => {
  const payload = action.payload ?? {};
  const [kinds] = useState<Record<string, FieldKind>>(() =>
    Object.fromEntries(Object.entries(payload).map(([name, value]) => [name, kindOf(value)])));

  const setSlot = (name: string, value: string | number | boolean) => {
    onChange({ ...action, payload: { ...payload, [name]: value } });
  };

  const entries = Object.entries(payload);

  return (
    <div className="primitive-action-fields" data-testid="primitive-action-fields">
      <p className="primitive-action-fields__type">
        Inline action: <code data-testid="primitive-action-type">{action.type}</code>
      </p>

      {entries.length === 0 && (
        <p className="primitive-action-fields__empty">This action takes no parameters.</p>
      )}

      {entries.map(([name, value]) => {
        const kind = kinds[name] ?? kindOf(value);
        const fieldId = `${idPrefix}-${name}`;

        if (kind === 'complex') {
          return (
            <div key={name} className="primitive-action-fields__field primitive-action-fields__field--complex">
              <span className="primitive-action-fields__label">{name}</span>
              <pre data-testid={`primitive-action-complex-${name}`}>{JSON.stringify(value, null, 2)}</pre>
              <p className="primitive-action-fields__hint">
                Structured values are kept exactly as saved; edit them through the API.
              </p>
            </div>
          );
        }

        if (kind === 'boolean') {
          return (
            <div key={name} className="primitive-action-fields__field">
              <label htmlFor={fieldId}>
                <input
                  id={fieldId}
                  type="checkbox"
                  checked={value === true}
                  disabled={disabled}
                  onChange={(event) => setSlot(name, event.target.checked)}
                />
                {name}
              </label>
            </div>
          );
        }

        return (
          <div key={name} className="primitive-action-fields__field">
            <ParameterizableField
              id={fieldId}
              label={name}
              value={String(value ?? '')}
              numeric={kind === 'number'}
              scope={scope}
              disabled={disabled}
              onChange={(next) => setSlot(name, coerce(next, kind))}
            />
          </div>
        );
      })}
    </div>
  );
};
