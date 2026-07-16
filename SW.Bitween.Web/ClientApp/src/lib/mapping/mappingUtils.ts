// ─── Mapping Utilities ────────────────────────────────────────────────────────

/**
 * Resolves a dot-separated path against a parsed JSON object.
 * Returns the string representation of the primitive value at that path,
 * or an empty string if the path does not exist or resolves to an object/array.
 */
export function getValueAtPath(obj: unknown, path: string): string {
  const parts = path.split('.');
  let current: unknown = obj;
  for (const part of parts) {
    if (current == null || typeof current !== 'object') return '';
    current = (current as Record<string, unknown>)[part];
  }
  if (current === null || current === undefined) return '';
  if (typeof current === 'object') return '';
  return String(current);
}
