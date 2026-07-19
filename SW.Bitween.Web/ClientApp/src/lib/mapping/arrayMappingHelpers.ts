import { ArrayMapping } from './types';

/** Recursively build the absolute source path for an ArrayMapping by traversing parentArrayId links. */
export function getFullSourcePath(amId: string, allMappings: ArrayMapping[]): string {
  const am = allMappings.find((m) => m.id === amId);
  if (!am) return '';
  if (!am.parentArrayId) return am.source;
  return `${getFullSourcePath(am.parentArrayId, allMappings)}.${am.source}`;
}

/** Recursively build the absolute target base path (dot-notation, no [*]) for an ArrayMapping. */
export function getFullTargetBase(amId: string, allMappings: ArrayMapping[]): string {
  const am = allMappings.find((m) => m.id === amId);
  if (!am) return '';
  if (!am.parentArrayId) return am.target;
  return `${getFullTargetBase(am.parentArrayId, allMappings)}.${am.target}`;
}

/** Return the relative sub-array keys available inside the first item of a parent array path. */
export function getItemArrayPaths(obj: any, parentFullSourcePath: string): string[] {
  if (!parentFullSourcePath) return [];
  const arr = navigateThroughArrays(obj, parentFullSourcePath);
  if (!Array.isArray(arr) || arr.length === 0) return [];
  const item = arr[0];
  if (!item || typeof item !== 'object' || Array.isArray(item)) return [];
  return Object.entries(item)
    .filter(([, v]) => Array.isArray(v) && (v as any[]).length > 0 && typeof (v as any[])[0] === 'object' && (v as any[])[0] !== null)
    .map(([k]) => k);
}

export function collectArrayPaths(obj: any, prefix = ''): string[] {
  if (!obj || typeof obj !== 'object') return [];
  if (Array.isArray(obj)) {
    return prefix ? [prefix] : [];
  }
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k;
    return collectArrayPaths(v, path);
  });
}

/** Navigate a dot-separated path in an object, returning the value at that path. */
export function navigatePath(obj: any, path: string): any {
  if (!path) return obj;
  return path.split('.').reduce((cur, key) => (cur != null && typeof cur === 'object' ? cur[key] : undefined), obj);
}

/**
 * Like navigatePath but transparently traverses arrays by using the first element.
 * e.g. navigateThroughArrays({orders: [{items: [...]}]}, 'orders.items') → items array
 */
export function navigateThroughArrays(obj: any, path: string): any {
  if (!path) return obj;
  return path.split('.').reduce((cur, key) => {
    if (cur == null) return undefined;
    const node = Array.isArray(cur) ? cur[0] : cur;
    if (node == null || typeof node !== 'object') return undefined;
    return node[key];
  }, obj);
}

/** Get the flat leaf-property names of items inside an array at `arrayPath`. */
export function getItemProperties(jsonStr: string, arrayPath: string): string[] {
  try {
    const root = JSON.parse(jsonStr);
    // First try: direct navigation to the path
    const arr = navigatePath(root, arrayPath);
    if (Array.isArray(arr) && arr.length > 0) {
      const first = arr[0];
      if (first && typeof first === 'object' && !Array.isArray(first)) {
        return flattenObjectKeys(first);
      }
    }
    // Second try: scan ALL leaf paths in the JSON and filter to those under arrayPath
    // This handles cases where the path traversal goes through intermediate arrays
    const allPaths = getAllLeafPaths(root);
    const prefix = `${arrayPath}.`;
    const nested = allPaths
      .filter((p) => p.startsWith(prefix))
      .map((p) => p.slice(prefix.length))
      .filter((p) => p.length > 0 && !p.includes('.'));
    return [...new Set(nested)];
  } catch {
    return [];
  }
}

/**
 * Collect all leaf paths in an object, traversing through arrays by using the first element.
 * E.g. {a: {b: [{c: 1}]}} → ['a.b.c']
 */
export function getAllLeafPaths(obj: any, prefix = ''): string[] {
  if (!obj || typeof obj !== 'object') return prefix ? [prefix] : [];
  if (Array.isArray(obj)) {
    if (obj.length === 0) return [];
    return getAllLeafPaths(obj[0], prefix);
  }
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k;
    return getAllLeafPaths(v, path);
  });
}

/** Recursively flatten object keys using dot notation. Only leaf (scalar) paths are
 * returned — intermediate object keys are excluded. Array-valued keys are also excluded
 * to prevent mistakenly mapping an array field to a scalar target field. */
export function flattenObjectKeys(obj: any, prefix = ''): string[] {
  if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return prefix ? [prefix] : [];
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k;
    if (Array.isArray(v)) return []; // exclude array-valued keys
    if (v && typeof v === 'object') {
      return flattenObjectKeys(v, path); // recurse without emitting the intermediate key
    }
    return [path];
  });
}

export function generateExample(
  source: string,
  target: string,
  alias: string,
  hasFilter: boolean,
  filterField: string,
  filterOp: string,
  filterValue: string
): string {
  const lines = [`"${target}": [`];
  lines.push(`  {{- for ${alias} in ${source} -}}`);
  if (hasFilter && filterField) {
    lines.push(`  {{- if ${alias}.${filterField} ${filterOp} ${filterValue} -}}`);
  }
  lines.push('  {');
  lines.push('    // ... field mappings ...');
  lines.push('  },');
  if (hasFilter && filterField) lines.push('  {{- end -}}');
  lines.push('  {{- end -}}');
  lines.push(']');
  return lines.join('\n');
}
