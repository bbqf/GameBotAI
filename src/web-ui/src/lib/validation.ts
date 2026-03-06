import { ApiError, ApiValidationError } from './api';
import type { ConditionExpression, SequenceFlowUpsertRequest } from '../types/sequenceFlow';

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
        errors.push(`Step "${step.stepId}" requires an action payload reference.`);
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
