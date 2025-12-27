export type AttributeDataType = 'string' | 'number' | 'boolean' | 'enum';

export type AttributeConstraints = {
  min?: number;
  max?: number;
  pattern?: string;
  allowedValues?: string[];
  defaultValue?: unknown;
};

export type AttributeDefinition = {
  key: string;
  label: string;
  dataType: AttributeDataType;
  required?: boolean;
  constraints?: AttributeConstraints;
  helpText?: string;
};

export type ActionType = {
  key: string;
  displayName: string;
  description?: string;
  version?: string;
  attributeDefinitions: AttributeDefinition[];
};

export type ValidationMessage = {
  field?: string;
  severity?: 'error' | 'warning';
  message: string;
};

export type ActionDto = {
  id: string;
  name: string;
  gameId: string;
  type: string;
  attributes: Record<string, unknown>;
  validationStatus?: 'valid' | 'invalid';
  validationMessages?: ValidationMessage[];
  createdBy?: string;
  updatedBy?: string;
  updatedAt?: string;
};

export type ActionCreate = {
  name: string;
  gameId: string;
  type: string;
  attributes: Record<string, unknown>;
};

export type ActionUpdate = ActionCreate;

export type ActionTypeCatalog = {
  version?: string;
  items: ActionType[];
};

export type ActionListParams = {
  type?: string;
  gameId?: string;
};

export type ValidationResult = {
  status: 'valid' | 'invalid';
  messages?: ValidationMessage[];
};
