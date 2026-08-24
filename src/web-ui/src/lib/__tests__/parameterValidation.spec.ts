import { ApiError } from '../api';
import { parseParameterErrors, parameterErrorFor, parameterErrorSummary } from '../validation';

/**
 * Feature 078 (FR-029): the backend anchors each parameter problem to a field path and parameter
 * name. These cover the parsing that lets an editor render the message at the field it concerns
 * instead of as one form-level banner.
 */
const parameterError = (details: unknown[], code = 'unresolvable_parameter_reference') =>
  new ApiError(400, 'bad request', undefined, { error: code, message: 'nope', details });

describe('parseParameterErrors', () => {
  it('keys issues by the field path the backend reported', () => {
    const parsed = parseParameterErrors(
      parameterError([
        {
          code: 'unresolvable_parameter_reference',
          message: "Step '0': 'typo' is not declared here and is not a queue built-in.",
          fieldPath: 'ensureEmulatorRunning.adbSerial',
          parameterName: 'typo',
        },
      ]),
    );

    expect(parsed).not.toBeNull();
    expect(parsed!.code).toBe('unresolvable_parameter_reference');
    expect(parsed!.byFieldPath['ensureEmulatorRunning.adbSerial']).toHaveLength(1);
    expect(parsed!.byFieldPath['ensureEmulatorRunning.adbSerial'][0].parameterName).toBe('typo');
  });

  it('collects issues that name no field as general', () => {
    const parsed = parseParameterErrors(
      parameterError(
        [{ code: 'invalid_parameter_declaration', message: "parameter name 'iteration' is reserved" }],
        'invalid_parameter_declaration',
      ),
    );

    expect(parsed!.general).toHaveLength(1);
    expect(parsed!.byFieldPath).toEqual({});
  });

  it('groups multiple issues on the same field', () => {
    const parsed = parseParameterErrors(
      parameterError([
        { code: 'a', message: 'first', fieldPath: 'swipe.startX' },
        { code: 'b', message: 'second', fieldPath: 'swipe.startX' },
      ]),
    );

    expect(parsed!.byFieldPath['swipe.startX'].map((i) => i.message)).toEqual(['first', 'second']);
  });

  it('returns null for a non-parameter API error so existing handling is untouched', () => {
    expect(parseParameterErrors(new ApiError(400, 'plain failure'))).toBeNull();
    expect(parseParameterErrors(new ApiError(409, 'conflict', undefined, { error: 'already_running' }))).toBeNull();
  });

  it('returns null for a rejection that is not an ApiError at all', () => {
    expect(parseParameterErrors(new Error('network down'))).toBeNull();
    expect(parseParameterErrors(undefined)).toBeNull();
  });

  it('ignores malformed detail entries rather than throwing', () => {
    const parsed = parseParameterErrors(
      parameterError([null, 'nonsense', { code: 'x' }, { code: 'y', message: 'kept', fieldPath: 'keyInput.key' }]),
    );

    expect(parsed!.byFieldPath['keyInput.key']).toHaveLength(1);
    expect(parsed!.general).toHaveLength(0);
  });
});

describe('parameterErrorFor', () => {
  it('returns the message for the requested field and nothing for a clean one', () => {
    const parsed = parseParameterErrors(
      parameterError([{ code: 'x', message: 'bad serial', fieldPath: 'ensureEmulatorRunning.adbSerial' }]),
    );

    expect(parameterErrorFor(parsed, 'ensureEmulatorRunning.adbSerial')).toBe('bad serial');
    expect(parameterErrorFor(parsed, 'keyInput.key')).toBeUndefined();
    expect(parameterErrorFor(null, 'anything')).toBeUndefined();
  });
});

describe('parameterErrorSummary', () => {
  it('joins general and field issues into one form-level message', () => {
    const parsed = parseParameterErrors(
      parameterError([
        { code: 'x', message: 'general problem' },
        { code: 'y', message: 'field problem', fieldPath: 'swipe.startX' },
      ]),
    );

    const summary = parameterErrorSummary(parsed);

    expect(summary).toContain('general problem');
    expect(summary).toContain('field problem');
  });

  it('returns undefined when there is nothing to show', () => {
    expect(parameterErrorSummary(null)).toBeUndefined();
  });
});
