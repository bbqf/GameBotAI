import React from 'react';
import type { SequencePrimitiveActionPayload } from '../../types/sequenceFlow';
import type { ParameterScopeEntry } from '../parameters/types';
import { ParameterizableField } from '../parameters/ParameterizableField';

/** The WaitForStill step type (feature 174). */
export const WAIT_FOR_STILL_TYPE = 'WaitForStill';

/** The default values of a new step (FR-008). They are the same as the service defaults. */
export const WAIT_FOR_STILL_DEFAULTS = { stableMs: 300, maxDiff: 0.01, timeoutMs: 5000 } as const;

export const isWaitForStillActionType = (type: string): boolean =>
  type.toLowerCase() === WAIT_FOR_STILL_TYPE.toLowerCase();

/** The payload of a new WaitForStill step. It has no region, so the whole screen is watched. */
export const createDefaultWaitForStillAction = (): SequencePrimitiveActionPayload => ({
  type: WAIT_FOR_STILL_TYPE,
  schemaVersion: '1',
  payload: { ...WAIT_FOR_STILL_DEFAULTS },
});

const REGION_KEYS = ['x', 'y', 'width', 'height'] as const;
const NUMERIC_LITERAL = /^-?\d+(?:\.\d+)?$/;

const asText = (value: unknown): string => (value === undefined || value === null ? '' : String(value));

/** A plain number stays a number. A `{{name}}` reference stays a string. */
const coerce = (raw: string): string | number => (NUMERIC_LITERAL.test(raw.trim()) ? Number(raw.trim()) : raw);

export type WaitForStillFieldsProps = {
  action: SequencePrimitiveActionPayload;
  onChange: (action: SequencePrimitiveActionPayload) => void;
  scope: ParameterScopeEntry[];
  disabled?: boolean;
  idPrefix: string;
};

/**
 * Field form of the WaitForStill step (feature 174). The step waits until a part of the screen, or the whole
 * screen, stops changing. The region is optional: with no value in the four region fields the whole screen is
 * watched. Every field takes a parameter reference.
 */
export const WaitForStillFields: React.FC<WaitForStillFieldsProps> = ({ action, onChange, scope, disabled, idPrefix }) => {
  const payload = action.payload ?? {};
  const region = (payload.region && typeof payload.region === 'object' ? payload.region : {}) as Record<string, unknown>;

  const setPayload = (next: Record<string, unknown>) => onChange({ ...action, payload: next });

  const setSlot = (name: string, raw: string) => {
    const { [name]: _removed, ...rest } = payload;
    void _removed;
    setPayload(raw.trim() === '' ? rest : { ...rest, [name]: coerce(raw) });
  };

  const setRegionSlot = (name: string, raw: string) => {
    const { [name]: _removed, ...regionRest } = region;
    void _removed;
    const nextRegion = raw.trim() === '' ? regionRest : { ...regionRest, [name]: coerce(raw) };
    const { region: _region, ...rest } = payload;
    void _region;
    setPayload(Object.keys(nextRegion).length === 0 ? rest : { ...rest, region: nextRegion });
  };

  return (
    <div className="wait-for-still-fields" data-testid="wait-for-still-fields">
      <p className="primitive-action-fields__type">
        Inline action: <code data-testid="primitive-action-type">{action.type}</code>
      </p>
      <fieldset className="wait-for-still-fields__region" data-testid={`${idPrefix}-region`}>
        <legend>region (empty: whole screen)</legend>
        {REGION_KEYS.map((key) => (
          <ParameterizableField
            key={key}
            id={`${idPrefix}-region-${key}`}
            label={`region ${key}`}
            value={asText(region[key])}
            numeric
            scope={scope}
            disabled={disabled}
            onChange={(next) => setRegionSlot(key, next)}
          />
        ))}
      </fieldset>
      {(['stableMs', 'maxDiff', 'timeoutMs'] as const).map((name) => (
        <ParameterizableField
          key={name}
          id={`${idPrefix}-${name}`}
          label={name}
          value={asText(payload[name])}
          numeric
          scope={scope}
          disabled={disabled}
          onChange={(next) => setSlot(name, next)}
        />
      ))}
    </div>
  );
};
