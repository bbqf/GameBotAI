import { EDITOR_PATHS, pathBelongsToStepType } from '../components/parameters/fieldTemplatePaths';
import type { ParsedParameterErrors } from '../lib/validation';

/** Service messages for step fields: step order, then field path. */
export type StepFieldErrors = Record<number, Record<string, string>>;

const STEP_PREFIX = /^\s*Step\s+(\d+)\s*:/i;

/** Reads the step order from the `Step {order}:` start of a service message. */
export const parseStepOrder = (message: string): number | undefined => {
  const match = STEP_PREFIX.exec(message);
  return match ? Number(match[1]) : undefined;
};

/**
 * Feature 169: maps each parameter issue to the field of the step that its message names.
 * The step order comes from the message text. The field path comes from the issue details.
 * An issue is left out (the form summary still shows it) when:
 * - the message has no `Step {order}:` start,
 * - no step has that order,
 * - the path has no control in the editor, or
 * - the path does not belong to the type of that step.
 *
 * @param parsed Result of `parseParameterErrors`.
 * @param stepTypes The type of each step, in order.
 * @returns The messages by step order and path, or `undefined` when no issue maps to a field.
 */
export const mapParameterErrorsToSteps = (
  parsed: ParsedParameterErrors | null,
  stepTypes: readonly string[],
): StepFieldErrors | undefined => {
  if (!parsed) return undefined;
  const result: StepFieldErrors = {};
  for (const [path, issues] of Object.entries(parsed.byFieldPath)) {
    if (!EDITOR_PATHS.includes(path)) continue;
    for (const issue of issues) {
      const order = parseStepOrder(issue.message);
      if (order === undefined || order >= stepTypes.length) continue;
      if (!pathBelongsToStepType(path, stepTypes[order])) continue;
      const forStep = (result[order] ??= {});
      forStep[path] ??= issue.message;
    }
  }
  return Object.keys(result).length > 0 ? result : undefined;
};
