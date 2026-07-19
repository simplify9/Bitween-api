import { TreeNode } from './mappingPreview';
import { LookupDictionary, LookupEntry } from './types';

// ─── Fixed-item inline panel types ───────────────────────────────────────────

export type DraftFieldMode = 'source' | 'fixed' | 'array' | 'partner' | 'global';

export interface DraftField {
  key: string;
  mode: DraftFieldMode;
  value: string;
  transform: string;
  showTransform: boolean;
  // partner mode:
  partnerPropKey?: string;
  // global mode:
  globalSetId?: string;
  globalKey?: string;
  // source lookup dictionary:
  lookupDictionary?: LookupDictionary;
  // array mode only:
  nestedChildNode?: TreeNode;
  nestedItems?: DraftField[][];
  addingItem?: DraftField[] | null;
  editingSubIdx?: number | null;
}

export const FIXED_ITEM_MAX_DEPTH = 2; // 0-based: depths 0,1,2 → 3 nesting levels

/** Recursively collect leaf keys from tree children, using dot-notation for nested objects. */
function collectLeafKeysFromChildren(children: TreeNode[], prefix = ''): string[] {
  return children.flatMap((c) => {
    const path = prefix ? `${prefix}.${c.key}` : c.key;
    if (c.type === 'leaf') return [path];
    if (c.type === 'object') return collectLeafKeysFromChildren(c.children, path);
    return []; // skip arrays — handled separately
  });
}

export function initDraftFieldsFromNode(node: TreeNode | undefined): DraftField[] {
  if (!node) return [{ key: '', mode: 'fixed' as DraftFieldMode, value: '', transform: '', showTransform: false }];
  const leafKeys = collectLeafKeysFromChildren(node.children);
  if (leafKeys.length > 0) {
    return leafKeys.map((key) => ({ key, mode: 'fixed' as DraftFieldMode, value: '', transform: '', showTransform: false }));
  }
  return [{ key: '', mode: 'fixed' as DraftFieldMode, value: '', transform: '', showTransform: false }];
}

function parseLookupEntries(dictStr: string): LookupEntry[] {
  const entries: LookupEntry[] = [];
  const re = /"([^"]+)": "([^"]*)"/g;
  let m;
  while ((m = re.exec(dictStr)) !== null) entries.push({ from: m[1], to: m[2] });
  return entries;
}

/** Set a value at a dotted path inside a record, creating intermediate objects as needed. */
function setRecordByPath(obj: Record<string, unknown>, path: string, value: unknown): void {
  const parts = path.split('.');
  let cur: Record<string, unknown> = obj;
  for (let i = 0; i < parts.length - 1; i++) {
    if (typeof cur[parts[i]] !== 'object' || cur[parts[i]] === null || Array.isArray(cur[parts[i]])) {
      cur[parts[i]] = {};
    }
    cur = cur[parts[i]] as Record<string, unknown>;
  }
  cur[parts[parts.length - 1]] = value;
}

export function draftFieldsToRecord(fields: DraftField[]): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  for (const df of fields) {
    const k = df.key.trim();
    if (!k) continue;
    if (df.mode === 'array') {
      setRecordByPath(result, k, (df.nestedItems ?? []).map((itemFields) => draftFieldsToRecord(itemFields)));
    } else if (df.mode === 'partner') {
      if (!df.partnerPropKey?.trim()) continue;
      setRecordByPath(result, k, `{{__partner__?.${df.partnerPropKey.trim()}}}`);
    } else if (df.mode === 'global') {
      if (!df.globalSetId?.trim() || !df.globalKey?.trim()) continue;
      setRecordByPath(result, k, `{{__globals__?.${df.globalSetId.trim()}["${df.globalKey.trim()}"]}}`);
    } else if (df.mode === 'source') {
      if (!df.value.trim()) continue;
      const src = df.value.trim();
      const lkp = df.lookupDictionary;
      if (lkp?.entries?.length) {
        const valid = lkp.entries.filter(({ from, to }) => from.trim() !== '' && to.trim() !== '');
        if (valid.length > 0) {
          const entriesStr = valid.map(({ from, to }) => `"${from}": "${to}"`).join(', ');
          if (lkp.fallback === 'null') {
            setRecordByPath(result, k, `{{$__e = { ${entriesStr} }; $__e[${src}]}}`);
          } else {
            const fb = lkp.fallback === 'custom' ? `"${lkp.fallbackValue ?? ''}"` : src;
            setRecordByPath(result, k, `{{$__e = { ${entriesStr} }; ($__e[${src}] ?? ${fb})}}`);
          }
          continue;
        }
      }
      const expr = df.transform.trim()
        ? df.transform.trim().replace(/\bvalue\b/g, src)
        : src;
      setRecordByPath(result, k, `{{${expr}}}`);
    } else {
      if (df.value.trim() === '') continue;
      const raw = df.value.trim();
      const n = Number(raw);
      if (raw !== '' && !isNaN(n)) setRecordByPath(result, k, n);
      else if (raw === 'true') setRecordByPath(result, k, true);
      else if (raw === 'false') setRecordByPath(result, k, false);
      else setRecordByPath(result, k, raw);
    }
  }
  return result;
}

/** Flatten a nested record to dotted-path keys, keeping arrays as single entries. */
function flattenRecordKeys(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k;
    if (Array.isArray(v)) return [path]; // arrays kept as-is
    if (v && typeof v === 'object') return flattenRecordKeys(v as Record<string, unknown>, path);
    return [path];
  });
}

/** Get a value at a dotted path from a nested record. */
function getRecordByPath(obj: Record<string, unknown>, path: string): unknown {
  return path.split('.').reduce((cur: Record<string, unknown> | undefined, k) => (
    cur != null && typeof cur === 'object' ? cur[k] as Record<string, unknown> | undefined : undefined
  ), obj as Record<string, unknown> | undefined);
}

export function recordToDraftFields(item: Record<string, unknown>, node: TreeNode): DraftField[] {
  const nodeLeafKeys = collectLeafKeysFromChildren(node.children);
  const arrayChildren = node.children.filter((c) => c.type === 'array');
  const itemKeys = flattenRecordKeys(item);
  const allKeys = [...new Set([...nodeLeafKeys, ...itemKeys])];
  return allKeys.map((key): DraftField => {
    const v = getRecordByPath(item, key);
    if (Array.isArray(v)) {
      const childNode = arrayChildren.find((c) => c.key === key);
      return {
        key, mode: 'array', value: '', transform: '', showTransform: false,
        nestedChildNode: childNode,
        nestedItems: v.map((subItem) =>
          childNode
            ? recordToDraftFields(subItem as Record<string, unknown>, childNode)
            : Object.entries(subItem as Record<string, unknown>).map(([k, sv]) => ({
                key: k, mode: 'fixed' as DraftFieldMode, value: String(sv ?? ''), transform: '', showTransform: false,
              }))
        ),
        addingItem: null,
      };
    }
    if (typeof v === 'string') {
      const m = v.match(/^\{\{(.+)\}\}$/);
      if (m) {
        const expr = m[1].trim();
        // Partner: {{__partner__?.propkey}}
        const partnerMatch = expr.match(/^__partner__\??\.([\w]+)$/);
        if (partnerMatch) return { key, mode: 'partner', value: '', transform: '', showTransform: false, partnerPropKey: partnerMatch[1] };
        // Global: {{__globals__?.setid["key"]}}
        const globalMatch = expr.match(/^__globals__\??\.([\w]+)\["([^"]+)"\]$/);
        if (globalMatch) return { key, mode: 'global', value: '', transform: '', showTransform: false, globalSetId: globalMatch[1], globalKey: globalMatch[2] };
        // Lookup (null fallback): $__e = { ... }; $__e[path]
        const lkpNull = expr.match(/^\$__e = \{ (.+) \}; \$__e\[(.+)\]$/);
        if (lkpNull) return { key, mode: 'source', value: lkpNull[2], transform: '', showTransform: false, lookupDictionary: { entries: parseLookupEntries(lkpNull[1]), fallback: 'null' } };
        // Lookup (custom fallback): $__e = { ... }; ($__e[path] ?? FB)
        const lkpFb = expr.match(/^\$__e = \{ (.+) \}; \(\$__e\[(.+)\] \?\? (.+)\)$/);
        if (lkpFb) {
          return { key, mode: 'source', value: lkpFb[2], transform: '', showTransform: false, lookupDictionary: {
            entries: parseLookupEntries(lkpFb[1]),
            fallback: 'custom',
            fallbackValue: lkpFb[3].replace(/^"(.*)"$/, '$1'),
          }};
        }
        return { key, mode: 'source', value: expr, transform: '', showTransform: false };
      }
    }
    return { key, mode: 'fixed', value: v !== undefined ? String(v) : '', transform: '', showTransform: false };
  });
}
