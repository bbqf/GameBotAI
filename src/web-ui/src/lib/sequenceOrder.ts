/**
 * Order-sensitive equality for two lists of sequence ids.
 *
 * Used by the queue editor's Reload Template flow to decide whether a reload would
 * actually change the queue's entries (and therefore needs a confirmation prompt).
 */
export const sameSequenceOrder = (a: readonly string[], b: readonly string[]): boolean => {
  if (a.length !== b.length) return false;
  return a.every((value, index) => value === b[index]);
};
