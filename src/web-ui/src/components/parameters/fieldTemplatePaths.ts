/**
 * Feature 169: the step fields that a person can set to a parameter in the command editor.
 *
 * The list mirrors `CommandStepFieldPaths` in `src/GameBot.Domain/Commands/CommandStep.cs`. The API
 * does not expose the list, so the editor keeps a copy. The test `FieldTemplatePathMirrorTests`
 * fails when the copy and the C# class are different. Keep one entry on each line, in the form
 * `{ path: '...', kind: '...' }`, because that test reads this file as text.
 */
import type { ParameterDeclaration } from './types';
import { effectiveKind } from './types';

export type FieldTemplateKind = 'integer' | 'decimal' | 'image';

export type ParametrizablePath = {
  path: string;
  kind: FieldTemplateKind;
};

export const PARAMETRIZABLE_PATHS: readonly ParametrizablePath[] = [
  { path: 'swipe.startX', kind: 'integer' },
  { path: 'swipe.startY', kind: 'integer' },
  { path: 'swipe.endX', kind: 'integer' },
  { path: 'swipe.endY', kind: 'integer' },
  { path: 'swipe.durationMs', kind: 'integer' },
  { path: 'waitForImage.timeoutMs', kind: 'integer' },
  { path: 'ensureEmulatorRunning.instanceIndex', kind: 'integer' },
  { path: 'ensureGameRunning.readinessTimeoutMs', kind: 'integer' },
  { path: 'primitiveTap.detectionTarget.confidence', kind: 'decimal' },
  { path: 'primitiveTap.detectionTarget.offsetX', kind: 'integer' },
  { path: 'primitiveTap.detectionTarget.offsetY', kind: 'integer' },
  { path: 'ensureGameRunning.readinessImage.confidence', kind: 'decimal' },
  { path: 'ensureGameRunning.readinessImage.offsetX', kind: 'integer' },
  { path: 'ensureGameRunning.readinessImage.offsetY', kind: 'integer' },
  { path: 'primitiveTap.detectionTarget.referenceImageId', kind: 'image' },
  { path: 'waitForImage.detectionTarget.referenceImageId', kind: 'image' },
];

/** The 12 paths that have a field in the editor. The 4 `ensureGameRunning` numeric paths have none. */
export const EDITOR_PATHS: readonly string[] = PARAMETRIZABLE_PATHS
  .map((p) => p.path)
  .filter((p) => !p.startsWith('ensureGameRunning.'));

export const NUMERIC_TEXT_HINT =
  'This parameter gives text. A numeric field needs a number. The save will fail.';
/** A plain text parameter keeps its old behavior: the service saves it and the value is checked at run time. */
export const PLAIN_TEXT_HINT =
  'This parameter gives text. A numeric field needs a number. The save works, but the run fails when the value is not a number.';
export const IMAGE_HINT ='This parameter is not an image picker.';

const findPath = (path: string): ParametrizablePath | undefined =>
  PARAMETRIZABLE_PATHS.find((p) => p.path === path);

/** True when the path is one of the image paths. */
export const isImagePath = (path: string): boolean => findPath(path)?.kind === 'image';

/** True when the path is in the mirror list. */
export const isParametrizablePath = (path: string): boolean => findPath(path) !== undefined;

export type ParameterFit = { fits: true } | { fits: false; hint: string };

/**
 * Tells if a parameter suits a field. The result is advice only: the service decides on save.
 * The rules follow the service rule for text in a numeric field.
 */
export const parameterFit = (
  declaration: Pick<ParameterDeclaration, 'kind' | 'type' | 'pattern'>,
  path: string,
): ParameterFit => {
  const kind = effectiveKind(declaration);
  if (isImagePath(path)) {
    return kind === 'image' ? { fits: true } : { fits: false, hint: IMAGE_HINT };
  }
  const givesNumber =
    !declaration.pattern && ((kind === 'number') || (kind === 'choice' && declaration.type === 'number'));
  if (givesNumber) return { fits: true };
  const plainText = kind === 'text' && !declaration.pattern;
  return { fits: false, hint: plainText ? PLAIN_TEXT_HINT : NUMERIC_TEXT_HINT };
};

const WHOLE_PLACEHOLDER = /^\s*\{\{\s*([A-Za-z_][\w.]*)\s*\}\}\s*$/;

/** The name in a whole placeholder such as `{{name}}`. Gives `undefined` for any other text. */
export const templateParameterName = (template: string | undefined): string | undefined => {
  if (!template) return undefined;
  return WHOLE_PLACEHOLDER.exec(template)?.[1];
};

const STEP_KEY_PREFIX: Record<string, string> = {
  Swipe: 'swipe.',
  PrimitiveTap: 'primitiveTap.',
  WaitForImage: 'waitForImage.',
  EnsureEmulatorRunning: 'ensureEmulatorRunning.',
  EnsureGameRunning: 'ensureGameRunning.',
};

/**
 * The type change rule: drop each key that belongs to a step type other than the new one. Keep all
 * other keys. Gives `undefined` when no key is left.
 */
export const dropForeignTemplateKeys = (
  templates: Record<string, string> | undefined,
  newStepType: string,
): Record<string, string> | undefined => {
  if (!templates) return undefined;
  const ownPrefix = STEP_KEY_PREFIX[newStepType];
  const otherPrefixes = Object.entries(STEP_KEY_PREFIX)
    .filter(([type]) => type !== newStepType)
    .map(([, prefix]) => prefix);
  const kept: Record<string, string> = {};
  for (const [key, value] of Object.entries(templates)) {
    const foreign = otherPrefixes.some((prefix) => key.startsWith(prefix)) && !(ownPrefix && key.startsWith(ownPrefix));
    if (!foreign) kept[key] = value;
  }
  return Object.keys(kept).length > 0 ? kept : undefined;
};

/** True when the path belongs to the step type, by the key prefix of the type. */
export const pathBelongsToStepType = (path: string, stepType: string): boolean => {
  const prefix = STEP_KEY_PREFIX[stepType];
  return prefix !== undefined && path.startsWith(prefix);
};
