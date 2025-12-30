import React, { useEffect, useRef } from 'react';
import { ActionType, ValidationMessage } from '../../types/actions';
import { GameDto } from '../../services/games';
import { validateAttribute } from '../../services/validation';
import { FormActions, FormSection } from '../unified/FormLayout';
import { FieldRenderer } from './FieldRenderer';
import { useAdbDevices } from '../../services/useAdbDevices';

export type ActionFormValue = {
  name: string;
  gameId: string;
  type: string;
  attributes: Record<string, unknown>;
};

export type ActionFormProps = {
  actionTypes: ActionType[];
  games: GameDto[];
  value: ActionFormValue;
  errors?: ValidationMessage[];
  loading?: boolean;
  submitting?: boolean;
  onChange: (next: ActionFormValue) => void;
  onSubmit?: () => void;
  onCancel?: () => void;
  extraActions?: React.ReactNode;
};

const now = (): number => (typeof performance !== 'undefined' && typeof performance.now === 'function' ? performance.now() : Date.now());
const logPerf = (label: string, durationMs: number, meta?: Record<string, unknown>): void => {
  const rounded = Math.max(0, Math.round(durationMs));
  if (!Number.isFinite(rounded)) return;
  const suffix = meta ? ` ${JSON.stringify(meta)}` : '';
  if (typeof console !== 'undefined' && typeof console.debug === 'function') {
    console.debug(`[perf] ${label}: ${rounded}ms${suffix}`);
  }
};

const getFieldError = (errors: ValidationMessage[] | undefined, field: string): string | undefined => {
  if (!errors) return undefined;
  const match = errors.find((e) => e.field === field);
  return match?.message;
};

export const ActionForm: React.FC<ActionFormProps> = ({
  actionTypes,
  value,
  errors,
  loading,
  submitting,
  onChange,
  onSubmit,
  onCancel,
  extraActions,
  games
}) => {
  const renderMark = useRef<number>(now());
  renderMark.current = now();
  const selectedType = actionTypes.find((t) => t.key === value.type);
  const isConnectType = selectedType?.key === 'connect-to-game';
  const { loading: devicesLoading, devices, error: devicesError, refresh } = useAdbDevices(isConnectType);
  const hasGames = games.length > 0;

  useEffect(() => {
    if (loading) return;
    const elapsed = now() - renderMark.current;
    const fieldCount = selectedType?.attributeDefinitions.length ?? 0;
    logPerf('action-form.render', elapsed, { type: value.type || 'none', fields: fieldCount });
  }, [loading, selectedType, value.type]);

  const filterAttributesForType = (nextType?: ActionType) => {
    if (!nextType) return { nextAttrs: {}, dropped: true };
    const nextAttrs: Record<string, unknown> = {};
    let dropped = false;
    const definedKeys = new Set<string>();
    for (const def of nextType.attributeDefinitions) {
      definedKeys.add(def.key);
      const existing = value.attributes[def.key];
      const issue = validateAttribute(def, existing);
      if (!issue && existing !== undefined) nextAttrs[def.key] = existing;
      else if (existing !== undefined) dropped = true;
    }
    for (const key of Object.keys(value.attributes)) {
      if (!definedKeys.has(key)) dropped = true;
    }
    return { nextAttrs, dropped };
  };

  const handleTypeChange = (nextTypeKey: string) => {
    if (nextTypeKey === value.type) return;
    const nextType = actionTypes.find((t) => t.key === nextTypeKey);
    const hasAttributes = Object.keys(value.attributes).length > 0;
    const { nextAttrs, dropped } = filterAttributesForType(nextType);

    if (hasAttributes && dropped) {
      const confirmDrop = window.confirm('Changing the action type will reset incompatible fields. Continue?');
      if (!confirmDrop) return;
    }

    onChange({ ...value, type: nextTypeKey, attributes: nextAttrs });
  };

  const updateAttributes = (key: string, newValue: unknown) => {
    const next = { ...value.attributes };
    if (newValue === undefined || newValue === '') {
      delete next[key];
    } else {
      next[key] = newValue;
    }
    onChange({ ...value, attributes: next });
  };

  const renderFields = () => {
    if (!selectedType) return null;
    return selectedType.attributeDefinitions.map((def) => {
      if (isConnectType && def.key === 'adbSerial') {
        const stringVal = typeof value.attributes[def.key] === 'string' ? (value.attributes[def.key] as string) : '';
        const listId = 'adb-serial-options';
        return (
          <div className="field" key={def.key}>
            <label htmlFor={def.key}>ADB Serial *</label>
            <input
              id={def.key}
              list={listId}
              value={stringVal}
              onChange={(e) => updateAttributes(def.key, e.target.value)}
              placeholder="Select or enter a device serial"
              aria-invalid={Boolean(getFieldError(errors, def.key))}
              aria-describedby={devicesError ? `${def.key}-error` : undefined}
              disabled={submitting}
            />
            <datalist id={listId}>
              {devices.map((d) => (
                <option key={d.serial} value={d.serial}>{d.serial}{d.state ? ` (${d.state})` : ''}</option>
              ))}
            </datalist>
            {devicesLoading && <div className="form-hint">Loading device suggestions...</div>}
            {!devicesLoading && devices.length === 0 && !devicesError && (
              <div className="form-hint">No devices detected; enter a serial manually.</div>
            )}
            {devicesError && (
              <div id={`${def.key}-error`} className="form-error" role="alert">{devicesError}</div>
            )}
            <div className="form-actions-inline">
              <button type="button" onClick={() => { refresh(); }} disabled={devicesLoading}>Refresh devices</button>
            </div>
            {getFieldError(errors, def.key) && (
              <div className="field-error" role="alert">{getFieldError(errors, def.key)}</div>
            )}
          </div>
        );
      }

      return (
        <FieldRenderer
          key={def.key}
          definition={def}
          value={value.attributes[def.key]}
          error={getFieldError(errors, def.key)}
          onChange={updateAttributes}
        />
      );
    });
  };

  return (
    <form
      className="action-form"
      aria-label="Action form"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit?.();
      }}
    >
      <FormSection title="Basics" description="Required identifiers for the action." id="action-basics">
        <div className="field">
          <label htmlFor="action-game">Game *</label>
          <select
            id="action-game"
            aria-invalid={Boolean(getFieldError(errors, 'gameId'))}
            aria-describedby={getFieldError(errors, 'gameId') ? 'action-game-error' : undefined}
            value={value.gameId}
            onChange={(e) => onChange({ ...value, gameId: e.target.value })}
            disabled={submitting || loading}
          >
            <option value="" disabled>Select a game</option>
            {games.map((g) => (
              <option key={g.id} value={g.id}>{g.name}</option>
            ))}
          </select>
          {!hasGames && !loading && (
            <div className="form-hint">No games available. Add a game before creating this action.</div>
          )}
          {getFieldError(errors, 'gameId') && (
            <div id="action-game-error" className="field-error" role="alert">
              {getFieldError(errors, 'gameId')}
            </div>
          )}
        </div>

        <div className="field">
          <label htmlFor="action-name">Name *</label>
          <input
            id="action-name"
            aria-invalid={Boolean(getFieldError(errors, 'name'))}
            aria-describedby={getFieldError(errors, 'name') ? 'action-name-error' : undefined}
            value={value.name}
            onChange={(e) => onChange({ ...value, name: e.target.value })}
            disabled={submitting}
          />
          {getFieldError(errors, 'name') && (
            <div id="action-name-error" className="field-error" role="alert">
              {getFieldError(errors, 'name')}
            </div>
          )}
        </div>
      </FormSection>

      <FormSection title="Behavior" description="Select the action type and fill required attributes." id="action-behavior">
        <div className="field">
          <label htmlFor="action-type">Action Type *</label>
          <select
            id="action-type"
            aria-invalid={Boolean(getFieldError(errors, 'type'))}
            aria-describedby={getFieldError(errors, 'type') ? 'action-type-error' : undefined}
            value={value.type}
            onChange={(e) => handleTypeChange(e.target.value)}
            disabled={submitting}
          >
            <option value="" disabled>Select an action type</option>
            {actionTypes.map((t) => (
              <option key={t.key} value={t.key}>{t.displayName}</option>
            ))}
          </select>
          {getFieldError(errors, 'type') && (
            <div id="action-type-error" className="field-error" role="alert">
              {getFieldError(errors, 'type')}
            </div>
          )}
        </div>

        {loading && <div className="form-hint">Loading definitions...</div>}
        {!loading && renderFields()}
      </FormSection>

      {errors && errors.length > 0 && (
        <div className="form-error" role="alert">
          {errors.filter((e) => !e.field).map((e, idx) => (<div key={idx}>{e.message}</div>))}
        </div>
      )}

      <FormActions submitting={submitting} onCancel={onCancel} disabled={!hasGames}>
        {extraActions}
      </FormActions>
    </form>
  );
};
