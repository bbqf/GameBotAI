import { mapParameterErrorsToSteps, parseStepOrder } from '../parameterErrorMapping';
import type { ParsedParameterErrors } from '../../lib/validation';

const issue = (message: string, fieldPath: string) => ({
  code: 'text_parameter_in_numeric_field',
  message,
  fieldPath,
  parameterName: 'label',
});

const parsed = (path: string, message: string): ParsedParameterErrors => ({
  byFieldPath: { [path]: [issue(message, path)] },
  general: [],
  code: 'x',
});

const MESSAGE = (order: number, path: string) =>
  `Step ${order}: parameter 'label' gives text, but field '${path}' needs a number. Use a number parameter.`;

describe('parseStepOrder', () => {
  it('reads the order from the Step prefix', () => {
    expect(parseStepOrder("Step 3: parameter 'x' ...")).toBe(3);
    expect(parseStepOrder('Step 0: x')).toBe(0);
  });
  it('gives undefined when there is no prefix', () => {
    expect(parseStepOrder('parameter x gives text')).toBeUndefined();
  });
});

describe('mapParameterErrorsToSteps', () => {
  it('maps the message to the step that it names, by order and path', () => {
    const path = 'swipe.durationMs';
    const result = mapParameterErrorsToSteps(parsed(path, MESSAGE(1, path)), ['Swipe', 'Swipe']);
    expect(result).toEqual({ 1: { [path]: MESSAGE(1, path) } });
  });

  it('gives undefined for no parameter error', () => {
    expect(mapParameterErrorsToSteps(null, ['Swipe'])).toBeUndefined();
  });

  it('gives undefined when the message has no step prefix', () => {
    const path = 'swipe.durationMs';
    expect(mapParameterErrorsToSteps(parsed(path, 'parameter bad'), ['Swipe'])).toBeUndefined();
  });

  it('gives undefined when no step has the order', () => {
    const path = 'swipe.durationMs';
    expect(mapParameterErrorsToSteps(parsed(path, MESSAGE(5, path)), ['Swipe'])).toBeUndefined();
  });

  it('gives undefined when the path has no control in the editor', () => {
    const path = 'ensureGameRunning.readinessTimeoutMs';
    expect(mapParameterErrorsToSteps(parsed(path, MESSAGE(0, path)), ['EnsureGameRunning'])).toBeUndefined();
  });

  it('gives undefined when the path does not belong to the type of the named step', () => {
    const path = 'swipe.durationMs';
    expect(mapParameterErrorsToSteps(parsed(path, MESSAGE(0, path)), ['PrimitiveTap'])).toBeUndefined();
  });
});
