import { AttributeDefinition, AttributeDataType, AttributeConstraints, ValidationMessage } from '../types/actions';

export type FieldValidationIssue = {
  field: string;
  message: string;
};

const isEmpty = (v: unknown) => v === null || v === undefined || (typeof v === 'string' && v.trim().length === 0);

const validateNumberBounds = (value: number, constraints?: AttributeConstraints): string | undefined => {
  if (!constraints) return undefined;
    if (typeof constraints.min === 'number' && value < constraints.min) return `Must be at least ${constraints.min}`;
    if (typeof constraints.max === 'number' && value > constraints.max) return `Must be at most ${constraints.max}`;
  return undefined;
};

const validatePattern = (value: string, constraints?: AttributeConstraints): string | undefined => {
  if (!constraints?.pattern) return undefined;
  try {
    const re = new RegExp(constraints.pattern);
    if (!re.test(value)) return 'Does not match required pattern';
  } catch {
    /* ignore invalid regex definitions at runtime */
  }
  return undefined;
};

const validateEnum = (value: string, constraints?: AttributeConstraints): string | undefined => {
  if (!constraints?.allowedValues) return undefined;
  if (!constraints.allowedValues.includes(value)) return 'Value not allowed';
  return undefined;
};

export const coerceValue = (
  dataType: AttributeDataType,
  raw: string | boolean | number | undefined
): { value?: unknown; error?: string } => {
  if (raw === undefined) return { value: undefined };
  if (dataType === 'boolean') return { value: Boolean(raw) };
  if (dataType === 'number') {
    const num = typeof raw === 'number' ? raw : Number(raw);
    if (Number.isNaN(num)) return { error: 'Must be a number' };
    return { value: num };
  }
  if (dataType === 'enum') {
    if (typeof raw !== 'string') return { error: 'Must be a string option' };
    return { value: raw };
  }
  // string
  if (typeof raw !== 'string') return { value: String(raw ?? '') };
  return { value: raw };
};

export const validateAttribute = (
  def: AttributeDefinition,
  value: unknown
): FieldValidationIssue | undefined => {
  if (isEmpty(value)) {
    if (def.required) return { field: def.key, message: 'This field is required' };
    return undefined;
  }

  if (def.dataType === 'number') {
    if (typeof value !== 'number') return { field: def.key, message: 'Must be a number' };
    const bounds = validateNumberBounds(value, def.constraints);
    if (bounds) return { field: def.key, message: bounds };
    return undefined;
  }

  if (def.dataType === 'boolean') {
    if (typeof value !== 'boolean') return { field: def.key, message: 'Must be true or false' };
    return undefined;
  }

  if (def.dataType === 'enum') {
    if (typeof value !== 'string') return { field: def.key, message: 'Must be a string option' };
    const enumErr = validateEnum(value, def.constraints);
    if (enumErr) return { field: def.key, message: enumErr };
    return undefined;
  }

  // string
  if (typeof value !== 'string') return { field: def.key, message: 'Must be text' };
  const patErr = validatePattern(value, def.constraints);
  if (patErr) return { field: def.key, message: patErr };
  return undefined;
};

export const validateAttributes = (
  defs: AttributeDefinition[],
  attributes: Record<string, unknown>
): ValidationMessage[] => {
  const messages: ValidationMessage[] = [];
  for (const def of defs) {
    const issue = validateAttribute(def, attributes[def.key]);
    if (issue) messages.push({ field: issue.field, message: issue.message, severity: 'error' });
  }
  return messages;
};
