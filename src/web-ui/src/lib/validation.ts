import { ApiError, ApiValidationError } from './api';
import type { ConditionExpression, SequenceFlowUpsertRequest } from '../types/sequenceFlow';
import type { SequenceLinearStep } from '../types/sequenceFlow';

export type ParsedValidation = {
  byField: Record<string, string[]>;
  general: string[];
};

const keywordFieldMap: Record<string, string[]> = {
  // Block-related keywords to field associations (heuristics)
  elseSteps: ['blocks[].elseSteps'],
  thenSteps: ['blocks[].thenSteps'],
  cadenceMs: ['blocks[].cadenceMs'],
  maxIterations: ['blocks[].maxIterations'],
  timeoutMs: ['blocks[].timeoutMs'],
  condition: ['blocks[].condition'],
  steps: ['blocks[].steps'],
  type: ['blocks[].type']
};

const normalize = (s: string) => s.trim();

export const parseValidationErrors = (errors: Array<ApiValidationError | string>): ParsedValidation => {
  const res: ParsedValidation = { byField: {}, general: [] };
  for (const e of errors) {
    const msg = typeof e === 'string' ? e : e.message;
    const field = typeof e === 'string' ? undefined : e.field;
    if (field && field.length > 0) {
      const key = normalize(field);
      (res.byField[key] ??= []).push(msg);
      continue;
    }
    // Heuristic: scan for known keywords to assign to a synthetic field path
    const lower = msg.toLowerCase();
    let assigned = false;
    for (const kw of Object.keys(keywordFieldMap)) {
      if (lower.includes(kw.toLowerCase())) {
        const targets = keywordFieldMap[kw];
        for (const t of targets) {
          (res.byField[t] ??= []).push(msg);
        }
        assigned = true;
        break;
      }
    }
    if (!assigned) res.general.push(msg);
  }
  return res;
};

export const parseFromError = (err: unknown): ParsedValidation | null => {
  if (!(err instanceof ApiError)) return null;
  const errs = err.errors ?? [];
  return parseValidationErrors(errs);
};

/** One parameter problem the backend reported, already anchored to a field (feature 078). */
export type ParameterFieldIssue = {
  code: string;
  message: string;
  fieldPath?: string;
  parameterName?: string;
};

/** Parameter problems from a save, keyed by the dotted field path they belong to. */
export type ParsedParameterErrors = {
  /** Keyed by `fieldPath`; a field with no path lands in {@link general} instead. */
  byFieldPath: Record<string, ParameterFieldIssue[]>;
  /** Problems that name no field, e.g. a malformed declaration. */
  general: ParameterFieldIssue[];
  /** The top-level error code, e.g. `unresolvable_parameter_reference`. */
  code?: string;
};

/**
 * Extracts the parameter validation `details` a 400 carries (feature 078, FR-029), so an editor can
 * render each message at the field it concerns rather than as one form-level banner.
 *
 * Returns `null` for anything that is not a parameter error, so callers can fall through to their
 * existing error handling untouched.
 *
 * @param err The rejection from a save call.
 */
export const parseParameterErrors = (err: unknown): ParsedParameterErrors | null => {
  if (!(err instanceof ApiError)) return null;
  const payload = err.payload;
  if (!payload || typeof payload !== 'object') return null;

  const details = (payload as { details?: unknown }).details;
  if (!Array.isArray(details) || details.length === 0) return null;

  const result: ParsedParameterErrors = {
    byFieldPath: {},
    general: [],
    code: typeof (payload as { error?: unknown }).error === 'string'
      ? (payload as { error: string }).error
      : undefined,
  };

  for (const raw of details) {
    if (!raw || typeof raw !== 'object') continue;
    const detail = raw as Record<string, unknown>;
    const message = typeof detail.message === 'string' ? detail.message : undefined;
    if (!message) continue;

    const issue: ParameterFieldIssue = {
      code: typeof detail.code === 'string' ? detail.code : (result.code ?? 'parameter_error'),
      message,
      fieldPath: typeof detail.fieldPath === 'string' ? detail.fieldPath : undefined,
      parameterName: typeof detail.parameterName === 'string' ? detail.parameterName : undefined,
    };

    if (issue.fieldPath) {
      (result.byFieldPath[issue.fieldPath] ??= []).push(issue);
    } else {
      result.general.push(issue);
    }
  }

  // Only a payload that actually carried a parameter issue counts as a parameter error.
  return Object.keys(result.byFieldPath).length > 0 || result.general.length > 0 ? result : null;
};

/**
 * The message to show at one field, or undefined when that field is clean.
 *
 * @param parsed Result of {@link parseParameterErrors}; `null` yields undefined.
 * @param fieldPath Dotted path of the field being rendered, e.g. `ensureEmulatorRunning.adbSerial`.
 */
export const parameterErrorFor = (
  parsed: ParsedParameterErrors | null,
  fieldPath: string,
): string | undefined => parsed?.byFieldPath[fieldPath]?.[0]?.message;

/** A single combined message for form-level display, or undefined when there is nothing to show. */
export const parameterErrorSummary = (
  parsed: ParsedParameterErrors | null,
): string | undefined => {
  if (!parsed) return undefined;
  const all = [...parsed.general, ...Object.values(parsed.byFieldPath).flat()];
  return all.length > 0 ? all.map((i) => i.message).join(' ') : undefined;
};

export const validateConditionalFlow = (flow: Pick<SequenceFlowUpsertRequest, 'entryStepId' | 'steps' | 'links'>): string[] => {
  const errors: string[] = [];
  const stepIds = new Set(flow.steps.map((step) => step.stepId));

  if (!stepIds.has(flow.entryStepId)) {
    errors.push(`Entry step "${flow.entryStepId}" does not exist in the flow graph.`);
  }

  for (const link of flow.links) {
    if (!stepIds.has(link.sourceStepId)) {
      errors.push(`Link "${link.linkId}" has unresolved source step "${link.sourceStepId}".`);
    }
    if (!stepIds.has(link.targetStepId)) {
      errors.push(`Link "${link.linkId}" has unresolved target step "${link.targetStepId}".`);
    }
  }

  for (const step of flow.steps) {
    if (step.stepType === 'action' || step.stepType === 'command') {
      if (!step.payloadRef || !step.payloadRef.trim()) {
        errors.push(`Step "${step.stepId}" requires a primitive/action payload reference.`);
      }
    }

    if (step.stepType !== 'condition') {
      continue;
    }

    if (!step.condition) {
      errors.push(`Condition step "${step.stepId}" requires a condition expression.`);
      continue;
    }

    errors.push(...validateConditionExpression(step.condition, step.stepId));

    const trueLinks = flow.links.filter((link) => link.sourceStepId === step.stepId && link.branchType === 'true');
    const falseLinks = flow.links.filter((link) => link.sourceStepId === step.stepId && link.branchType === 'false');
    const hasBlankBranchTarget = trueLinks.some((link) => !link.targetStepId) || falseLinks.some((link) => !link.targetStepId);

    if (trueLinks.length !== 1 || falseLinks.length !== 1 || hasBlankBranchTarget) {
      errors.push(`Condition step "${step.stepId}" must define one true and one false branch.`);
    }
  }

  return errors;
};

const validateConditionExpression = (expression: ConditionExpression, stepId: string): string[] => {
  const errors: string[] = [];
  const nodeType = expression.nodeType;

  if (nodeType === 'operand') {
    const operand = expression.operand;
    if (!operand) {
      errors.push(`Condition step "${stepId}" has an operand node without operand metadata.`);
      return errors;
    }

    if (!operand.targetRef || !operand.targetRef.trim()) {
      errors.push(`Condition step "${stepId}" must set imageId/targetRef for operand "${operand.operandType}".`);
    }

    if (operand.operandType === 'image-detection') {
      if (!['present', 'absent'].includes((operand.expectedState ?? '').toLowerCase())) {
        errors.push(`Condition step "${stepId}" image-detection expectedState must be present or absent.`);
      }

      if (operand.threshold != null && (operand.threshold < 0 || operand.threshold > 1)) {
        errors.push(`Condition step "${stepId}" threshold must be between 0 and 1.`);
      }
    }

    return errors;
  }

  const children = expression.children ?? [];
  if ((nodeType === 'and' || nodeType === 'or') && children.length < 2) {
    errors.push(`Condition step "${stepId}" ${nodeType.toUpperCase()} node must include at least two children.`);
  }

  if (nodeType === 'not' && children.length !== 1) {
    errors.push(`Condition step "${stepId}" NOT node must include exactly one child.`);
  }

  for (const child of children) {
    errors.push(...validateConditionExpression(child, stepId));
  }

  return errors;
};

export const validatePerStepConditions = (steps: SequenceLinearStep[]): string[] => {
  const errors: string[] = [];
  const seenStepIds = new Set<string>();

  for (let index = 0; index < steps.length; index += 1) {
    const step = steps[index];
    const stepLabel = step.stepId || `index:${index}`;

    if (!step.stepId?.trim()) {
      errors.push(`Step at index ${index} requires stepId.`);
      continue;
    }

    if (seenStepIds.has(step.stepId)) {
      errors.push(`Duplicate step id '${step.stepId}'.`);
    }
    seenStepIds.add(step.stepId);

    if (step.condition?.type === 'imageVisible') {
      if (!step.condition.imageId?.trim()) {
        errors.push(`Step '${stepLabel}' imageVisible condition requires imageId.`);
      }
      if (step.condition.minSimilarity != null && (step.condition.minSimilarity < 0 || step.condition.minSimilarity > 1)) {
        errors.push(`Step '${stepLabel}' imageVisible minSimilarity must be between 0 and 1.`);
      }
    }

    if (step.condition?.type === 'commandOutcome') {
      const stepRef = step.condition.stepRef;
      if (!stepRef?.trim()) {
        errors.push(`Step '${stepLabel}' commandOutcome condition requires stepRef.`);
      } else {
        const refIndex = steps.findIndex((candidate) => candidate.stepId === stepRef);
        if (refIndex < 0) {
          errors.push(`Step '${stepLabel}' commandOutcome references unknown prior step '${stepRef}'.`);
        } else if (refIndex >= index) {
          errors.push(`Step '${stepLabel}' commandOutcome stepRef '${stepRef}' must reference a prior step.`);
        }
      }

      if (!['success', 'failed', 'skipped'].includes(step.condition.expectedState)) {
        errors.push(`Step '${stepLabel}' commandOutcome expectedState must be one of success|failed|skipped.`);
      }
    }
  }

  return errors;
};
