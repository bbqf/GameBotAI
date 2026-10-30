import React, { useEffect, useId, useMemo, useRef, useState } from 'react';
import { parameterFit, templateParameterName } from './fieldTemplatePaths';
import type { ParameterDeclaration } from './types';
import { QUEUE_BUILT_INS, displayLabel, toReference } from './types';

export type ParameterSourceControlProps = {
  /** Path of the field in `fieldTemplates`, for example `swipe.durationMs`. */
  path: string;
  /** Visible label of the field. Used in the accessible names. */
  label: string;
  /** The parameters that the command declares. */
  parameters: readonly ParameterDeclaration[];
  /** The placeholder now set for the path. */
  template: string | undefined;
  /** Gives the new placeholder. `undefined` removes the key. */
  onChange: (template: string | undefined) => void;
  disabled?: boolean;
  /** Message from the service for this path. */
  error?: string;
  /**
   * When set, the person cannot open the list and the text shows under the field. Used by an image
   * field that needs a stored image before it can use a parameter.
   */
  blockedMessage?: string;
  /** The plain input. It is hidden while a placeholder is set. */
  children: React.ReactNode;
};

const EMPTY_TEXT = 'This command has no parameters. Add one in the Parameters section.';

/**
 * Feature 169: lets a person set one step field to a parameter. In plain mode it shows the normal
 * input and a button. In parameter mode it shows a chip and a button that removes the parameter.
 * The hints are advice only: every row stays selectable and the service decides on save.
 */
export const ParameterSourceControl: React.FC<ParameterSourceControlProps> = ({
  path,
  label,
  parameters,
  template,
  onChange,
  disabled,
  error,
  blockedMessage,
  children,
}) => {
  const blocked = Boolean(blockedMessage) && !(template !== undefined && template !== '');
  const baseId = useId();
  const errorId = `${baseId}-error`;
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const openButtonRef = useRef<HTMLButtonElement>(null);
  const removeButtonRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const focusRemoveRef = useRef(false);

  const declared = useMemo(() => parameters.filter((p) => p.name.trim().length > 0), [parameters]);
  const name = templateParameterName(template);
  const hasTemplate = template !== undefined && template !== '';
  const shownName = name ?? template ?? '';
  const missing = hasTemplate
    && !declared.some((p) => p.name === name)
    && !QUEUE_BUILT_INS.some((b) => b.name === name);

  useEffect(() => {
    if (focusRemoveRef.current && hasTemplate) {
      focusRemoveRef.current = false;
      removeButtonRef.current?.focus();
    }
  }, [hasTemplate, template]);

  useEffect(() => {
    if (!open) return undefined;
    const close = (event: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  useEffect(() => {
    if (open) listRef.current?.querySelector<HTMLElement>('[role="option"]')?.focus();
  }, [open]);

  const select = (parameterName: string) => {
    focusRemoveRef.current = true;
    setOpen(false);
    onChange(toReference(parameterName));
  };

  const closeAndFocusButton = () => {
    setOpen(false);
    openButtonRef.current?.focus();
  };

  const onListKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const options = Array.from(listRef.current?.querySelectorAll<HTMLElement>('[role="option"]') ?? []);
    const index = options.indexOf(document.activeElement as HTMLElement);
    if (event.key === 'Escape') {
      event.preventDefault();
      closeAndFocusButton();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      options[Math.min(index + 1, options.length - 1)]?.focus();
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      options[Math.max(index - 1, 0)]?.focus();
    }
  };

  const describedBy = error ? errorId : undefined;

  const renderOption = (optionName: string, secondary: string | undefined, hint: string | undefined) => {
    const hintId = `${baseId}-hint-${optionName}`;
    return (
      <div
        key={optionName}
        role="option"
        tabIndex={-1}
        aria-selected={optionName === name}
        aria-describedby={hint ? hintId : undefined}
        className="parameter-source__option"
        onClick={() => select(optionName)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            select(optionName);
          }
        }}
      >
        <span className="parameter-source__option-name">{optionName}</span>
        {secondary && <span className="parameter-source__option-label"> {secondary}</span>}
        {hint && <span id={hintId} className="form-hint parameter-source__hint"> {hint}</span>}
      </div>
    );
  };

  return (
    <div className="parameter-source" ref={rootRef} data-path={path}>
      {hasTemplate ? (
        <div className="parameter-source__chip-row">
          <span className="parameter-source__chip" data-testid={`chip-${path}`}>{shownName}</span>
          {missing && <span className="field-error parameter-source__missing">Parameter not found</span>}
          <button
            type="button"
            ref={removeButtonRef}
            className="btn btn-secondary parameter-source__remove"
            aria-label={`Remove parameter from ${label}`}
            aria-describedby={describedBy}
            disabled={disabled}
            onClick={() => {
              onChange(undefined);
              setOpen(false);
            }}
          >
            Remove
          </button>
        </div>
      ) : (
        children
      )}
      {!hasTemplate && (
        <button
          type="button"
          ref={openButtonRef}
          className="btn btn-secondary parameter-source__open"
          aria-label={`Use a parameter for ${label}`}
          aria-haspopup="listbox"
          aria-expanded={open}
          aria-describedby={describedBy}
          disabled={disabled || blocked}
          onClick={() => setOpen((v) => !v)}
          onKeyDown={(e) => {
            if (e.key === 'ArrowDown' && !open) {
              e.preventDefault();
              setOpen(true);
            }
          }}
        >
          Use a parameter
        </button>
      )}
      {open && !hasTemplate && (
        <div className="parameter-source__popup">
          {declared.length === 0 && <p className="form-hint">{EMPTY_TEXT}</p>}
          <div
            role="listbox"
            aria-label="Parameters of this command"
            ref={listRef}
            tabIndex={-1}
            onKeyDown={onListKeyDown}
          >
            {declared.length > 0 && (
              <div role="group" aria-label="Parameters">
                <div className="parameter-source__group-title" aria-hidden="true">Parameters</div>
                {declared.map((p) => {
                  const fit = parameterFit(p, path);
                  return renderOption(p.name, p.label ? displayLabel(p) : undefined, fit.fits ? undefined : fit.hint);
                })}
              </div>
            )}
            <div role="group" aria-label="Queue values">
              <div className="parameter-source__group-title" aria-hidden="true">Queue values</div>
              {QUEUE_BUILT_INS.map((b) => renderOption(b.name, undefined, undefined))}
            </div>
          </div>
        </div>
      )}
      {blocked && <div className="form-hint">{blockedMessage}</div>}
      {error && <div id={errorId} className="field-error" role="alert">{error}</div>}
    </div>
  );
};
