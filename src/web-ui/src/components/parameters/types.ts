/**
 * Shared parameter types for the authoring UI (feature 078).
 *
 * These mirror the wire shapes served by the backend. The backend is the single implementation of
 * the resolution rules, so the UI never re-derives "what would this resolve to" itself — it renders
 * what `/parameter-scope` and the template detail response tell it.
 */

/** "text" values substitute verbatim; "number" values must occupy a whole field. */
export type ParameterValueType = 'text' | 'number';

/** A parameter a command or sequence declares. */
export type ParameterDeclaration = {
  name: string;
  type: ParameterValueType;
  default?: string | null;
  required: boolean;
  description?: string | null;
};

/**
 * A value supplied at one call site. `value: null` (or absent) means **inherit** — the row is left
 * alone and the value flows in from the enclosing scope. An empty string is a real, deliberate value.
 */
export type ParameterBinding = {
  name: string;
  value?: string | null;
};

/** Which scope layer supplied (or would supply) a value. */
export type ParameterOriginLayer =
  | 'queue'
  | 'entry'
  | 'sequence'
  | 'command'
  | 'loop'
  | 'default';

/** One name visible in a scope, with its effective value and where it came from. */
export type ParameterScopeEntry = {
  name: string;
  value?: string | null;
  originLayer: ParameterOriginLayer;
  declared: boolean;
  description?: string | null;
};

/** A non-blocking advisory returned alongside a successful save. */
export type ParameterWarning = {
  code: string;
  message: string;
  fieldPath?: string | null;
  parameterName?: string | null;
  entryIndex?: number | null;
};

/** The callee declarations for one command step, so a binding form needs no extra fetch. */
export type ParameterStepCallee = {
  stepId: string;
  commandId: string;
  commandName: string;
  parameters: ParameterDeclaration[];
};

/** Response of `GET /api/{commands|sequences}/{id}/parameter-scope`. */
export type ParameterScopeResponse = {
  entries: ParameterScopeEntry[];
  stepCallees?: ParameterStepCallee[];
};

/** Human-readable label for a scope layer, used in the effective-value preview. */
export const originLayerLabel = (layer: ParameterOriginLayer): string => {
  switch (layer) {
    case 'queue': return 'from the queue';
    case 'entry': return 'set on this entry';
    case 'sequence': return 'from the sequence';
    case 'command': return 'set on this step';
    case 'loop': return 'from the loop';
    case 'default': return 'declared default';
    default: return layer;
  }
};

/** Wraps a parameter name in the reference syntax, so no caller hand-types braces. */
export const toReference = (name: string): string => `{{${name}}}`;

/** The reserved queue built-in prefix; these names can be referenced but never declared. */
export const BUILT_IN_PREFIX = 'queue.';

/** True when a name is one of the read-only queue built-ins. */
export const isBuiltIn = (name: string): boolean => name.startsWith(BUILT_IN_PREFIX);

/**
 * The reserved queue built-ins, mirroring the backend's ParameterNameRules catalogue.
 *
 * Duplicated here so the editor can offer them before an entity has ever been saved (an unsaved
 * draft has no id to fetch a scope for). The backend remains authoritative: a name offered here that
 * it would reject is caught on save.
 */
export const QUEUE_BUILT_INS: ParameterScopeEntry[] = [
  {
    name: 'queue.emulatorSerial',
    value: null,
    originLayer: 'queue',
    declared: false,
    description: "The executing queue's bound ADB device serial.",
  },
  {
    name: 'queue.instanceName',
    value: null,
    originLayer: 'queue',
    declared: false,
    description: "The executing queue's LDPlayer instance name, when one is configured.",
  },
  {
    name: 'queue.instanceIndex',
    value: null,
    originLayer: 'queue',
    declared: false,
    description: "The executing queue's LDPlayer instance index, when one is configured.",
  },
  {
    name: 'queue.gameId',
    value: null,
    originLayer: 'queue',
    declared: false,
    description: 'The game linked to the executing queue, when one is linked.',
  },
];

/**
 * The names an editor may offer while editing an entity: its own declarations plus the queue
 * built-ins. Values are the declared defaults, since no run is in progress.
 *
 * @param declarations The entity's own parameter declarations.
 */
export const buildEditorScope = (
  declarations: readonly ParameterDeclaration[] | undefined,
): ParameterScopeEntry[] => [
  ...(declarations ?? [])
    .filter((d) => d.name.trim().length > 0)
    .map<ParameterScopeEntry>((d) => ({
      name: d.name,
      value: d.default ?? null,
      originLayer: d.default != null ? 'default' : 'entry',
      declared: true,
      description: d.description ?? null,
    })),
  ...QUEUE_BUILT_INS,
];
