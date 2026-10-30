import { useCallback, useState } from 'react';

export type FieldTemplateMap = Record<string, string>;

export type UseFieldTemplatesResult = {
  /** The whole map, with the keys that the panel has no control for. */
  templates: FieldTemplateMap;
  /** The placeholder for a path, or `undefined`. */
  get: (path: string) => string | undefined;
  /** Sets a placeholder. `undefined` removes the key. */
  set: (path: string, template: string | undefined) => void;
  /** The map for the confirm value, or `undefined` when it has no key. */
  result: () => FieldTemplateMap | undefined;
};

/**
 * Feature 169: the `fieldTemplates` state of one step panel. A panel changes only the keys that it
 * has a control for. It keeps all other keys, so a save does not lose them.
 */
export const useFieldTemplates = (initial: FieldTemplateMap | undefined): UseFieldTemplatesResult => {
  const [templates, setTemplates] = useState<FieldTemplateMap>(initial ?? {});

  const set = useCallback((path: string, template: string | undefined) => {
    setTemplates((current) => {
      const next = { ...current };
      if (template === undefined || template === '') delete next[path];
      else next[path] = template;
      return next;
    });
  }, []);

  const get = useCallback((path: string) => templates[path], [templates]);
  const result = useCallback(
    () => (Object.keys(templates).length > 0 ? templates : undefined),
    [templates],
  );

  return { templates, get, set, result };
};
