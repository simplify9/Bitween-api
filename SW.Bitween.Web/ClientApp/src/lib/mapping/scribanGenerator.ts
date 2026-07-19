import { ArrayMapping, FieldMapping, FilterOperator, LookupDictionary, LookupEntry } from './types';

/** Map of valuesSetId → (key → value) for enum lookups */
export type ValuesSetMap = Record<string, Record<string, string>>;

/** Map of dot-path → primitive type inferred from a JSON sample */
export type TypeMap = Record<string, 'string' | 'number' | 'boolean'>;

/**
 * Build a TypeMap by walking every leaf in a JSON sample.
 * Array items are represented with [*] in the path, e.g. "orders[*].price".
 */
export function buildTypeMap(json: string): TypeMap {
  try {
    return walkTypeMap(JSON.parse(json), '');
  } catch {
    return {};
  }
}

function walkTypeMap(node: any, prefix: string): TypeMap {
  if (node === null || node === undefined) return {};
  if (Array.isArray(node)) {
    if (node.length === 0) return {};
    const first = node[0];
    if (typeof first !== 'object') {
      // primitive array — record the element type at this prefix.
      // Named arrays are called with prefix already containing "[*]" (e.g. "prices[*]").
      // Root primitive arrays are called with prefix="" → use "[*]" as key so the
      // generator's `itemPrefix` ("[*]") resolves the type correctly.
      const t = typeof first;
      if (t === 'string' || t === 'number' || t === 'boolean') {
        const key = prefix || '[*]';
        return { [key]: t };
      }
      return {};
    }
    return walkTypeMap(first, prefix); // use first element as representative
  }
  if (typeof node === 'object') {
    const result: TypeMap = {};
    for (const [k, v] of Object.entries(node)) {
      const path = prefix ? `${prefix}.${k}` : k;
      if (Array.isArray(v)) {
        // descend into array items using [*] notation
        const inner = walkTypeMap(v, `${path}[*]`);
        Object.assign(result, inner);
      } else if (v !== null && typeof v === 'object') {
        Object.assign(result, walkTypeMap(v, path));
      } else if (typeof v === 'string') {
        result[path] = 'string';
      } else if (typeof v === 'number') {
        result[path] = 'number';
      } else if (typeof v === 'boolean') {
        result[path] = 'boolean';
      }
    }
    return result;
  }
  return {};
}

/**
 * Return a Scriban-safe inline cast expression when source and target types differ.
 * to_float is registered as a custom function in ScribanJsonHelper.cs.
 */
function castExpr(path: string, targetType: 'string' | 'number' | 'boolean', sourceType?: 'string' | 'number' | 'boolean'): string {
  if (targetType === 'number') {
    if (sourceType === 'boolean') return `(${path} == null ? null : (${path} ? 1 : 0))`;
    return `(${path} | to_float)`;  // string → number
  }
  if (targetType === 'boolean') {
    if (sourceType === 'string') return 'null'; // string → bool: always null
    if (sourceType === 'number') return `(${path} == 0 ? false : (${path} == 1 ? true : null))`;
    // generic / unknown source type
    return `(${path} != null && ${path} != '' && ${path} != 'false' && ${path} != '0')`;
  }
  // string target
  if (sourceType === 'boolean') return `(${path} == null ? null : (${path} ? "true" : "false"))`;
  return `("" + ${path})`;  // number → string, generic
}

// ─── Scriban template generator ───────────────────────────────────────────────

const OPERATOR_MAP: Record<FilterOperator, string> = {
  '==': '==',
  '!=': '!=',
  '>': '>',
  '>=': '>=',
  '<': '<',
  '<=': '<=',
};

function renderFilter(alias: string, filter: ArrayMapping['filter']): string {
  if (!filter) return '';
  const op = OPERATOR_MAP[filter.operator] ?? filter.operator;
  const val = typeof filter.value === 'number' ? filter.value : `"${filter.value}"`;
  return `${alias}.${filter.field} ${op} ${val}`;
}

function renderFieldValue(
  mapping: FieldMapping,
  alias?: string,
  valuesSetMap?: ValuesSetMap,
  targetType?: 'string' | 'number' | 'boolean',
  sourceType?: 'string' | 'number' | 'boolean'
): string {
  if (mapping.partnerPropKey) {
    if (targetType === 'number' || targetType === 'boolean') return 'null';
    return `{{ __partner__?.${mapping.partnerPropKey} | json }}`;
  }
  if (mapping.globalSetId && mapping.globalKey) {
    if (targetType === 'number' || targetType === 'boolean') return 'null';
    return `{{ __globals__?.${mapping.globalSetId}["${mapping.globalKey}"] | json }}`;
  }
  if (mapping.fixedValue !== undefined) {
    const num = Number(mapping.fixedValue);
    if (!isNaN(num) && mapping.fixedValue.trim() !== '') return String(num);
    if (mapping.fixedValue === 'true' || mapping.fixedValue === 'false') return mapping.fixedValue;
    return `"${mapping.fixedValue}"`;
  }
  const src = mapping.source;
  if (!src) return 'null';

  // inline dictionary lookup — user-defined key→value map (takes precedence over valuesSetId)
  if (mapping.lookupDictionary?.entries && mapping.lookupDictionary.entries.length > 0) {
    const path = alias ? `${alias}.${src}` : src.replace(/\[?\*\]?/g, '');

    // Format a single output value with the correct type for the Scriban dict literal
    const fmtVal = (v: string): string => {
      if (targetType === 'number') {
        const n = Number(v);
        return isNaN(n) ? `"${v}"` : String(n);
      }
      if (targetType === 'boolean') {
        return v === 'true' || v === '1' ? 'true' : 'false';
      }
      return `"${v}"`; // string (default)
    };

    const entries = mapping.lookupDictionary.entries
      .filter(({ from, to }) => from.trim() !== '' && to.trim() !== '')
      .map(({ from, to }) => `"${from}": ${fmtVal(to)}`)
      .join(', ');
    if (!entries) return `{{ ${path} | json }}`;
    const lkp = mapping.lookupDictionary;
    if (lkp.fallback === 'null') {
      return `{{ $__e = { ${entries} }; $__e[${path}] | json }}`;
    }
    const fb = fmtVal(lkp.fallbackValue ?? '');
    return `{{ $__e = { ${entries} }; ($__e[${path}] ?? ${fb}) | json }}`;
  }

  // enum / values-set lookup — bake the dictionary inline
  if (mapping.valuesSetId) {
    const path = alias
      ? `${alias}.${src}`
      : src.replace(/\[?\*\]?/g, '');
    const dict = valuesSetMap?.[mapping.valuesSetId];
    if (dict && Object.keys(dict).length > 0) {
      const entries = Object.entries(dict)
        .map(([k, v]) => `"${k}": "${v}"`)
        .join(', ');
      return `{{ $__e = { ${entries} }; ($__e[${path}] ?? ${path}) | json }}{{# enum:${mapping.valuesSetId} #}}`;
    }
    return `{{ ${path} | json }}{{# enum:${mapping.valuesSetId} #}}`;
  }

  // transform expression — replace "value" with the actual path
  if (mapping.transform) {
    const path = alias ? `${alias}.${src.split('.').pop()}` : src.replace(/\./g, '.');
    const transformedExpr = mapping.transform.replace(/\bvalue\b/g, path);
    if (targetType === 'number') {
      // null → null, bool → 0/1, object/array → null, string/number → to_float
      return `{{ $__t = (${transformedExpr}); ($__t == null ? null : (($__t | object.typeof) == "boolean" ? ($__t ? 1 : 0) : ((($__t | object.typeof) == "object" || ($__t | object.typeof) == "array") ? null : ($__t | to_float)))) | json }}`;
    }
    if (targetType === 'string') {
      // null → null, object/array → null, bool → "true"/"false", number → string
      return `{{ $__t = (${transformedExpr}); ($__t == null ? null : ((($__t | object.typeof) == "object" || ($__t | object.typeof) == "array") ? null : (($__t | object.typeof) == "boolean" ? ($__t ? "true" : "false") : ("" + $__t)))) | json }}`;
    }
    return `{{ ${transformedExpr} | json }}`;
  }

  // plain path — apply type cast when target type is known
  const path = alias
    ? `${alias}.${src}`
    : src.replace(/\[?\*\]?/g, '').replace(/^\.+/, ''); // strip [*] and any leading dot

  if (targetType) {
    // same type — no cast needed
    if (sourceType === targetType) return `{{ ${path} | json }}`;
    return `{{ ${castExpr(path, targetType, sourceType)} | json }}`;
  }
  return `{{ ${path} | json }}`;
}

function indent(lines: string, spaces: number): string {
  const pad = ' '.repeat(spaces);
  return lines
    .split('\n')
    .map((l) => pad + l)
    .join('\n');
}

/**
 * Generate a Scriban template from field + array mappings.
 * Pass valuesSetMap to bake enum dictionaries inline; omit for degraded passthrough.
 * Pass outputJson to enable automatic type-cast filters when source/target types differ.
 */
export function generateScriban(
  fieldMappings: FieldMapping[],
  arrayMappings: ArrayMapping[],
  valuesSetMap?: ValuesSetMap,
  outputJson?: string,
  inputJson?: string
): string {
  const typeMap: TypeMap = outputJson ? buildTypeMap(outputJson) : {};
  const sourceTypeMap: TypeMap = inputJson ? buildTypeMap(inputJson) : {};

  // ── Root-array output: single top-level AM with isRootOutput=true ──────────
  // When present, the whole template is just `[ ... ]` with no wrapping object.
  // Field mappings are ignored in this mode (root output is an array, not an object).
  const rootOutputAm = arrayMappings.find((am) => am.isRootOutput && !am.parentArrayId);
  if (rootOutputAm) {
    return renderRootArrayMapping(rootOutputAm, arrayMappings, valuesSetMap, typeMap, inputJson, sourceTypeMap);
  }

  const rootLines: string[] = ['{'];

  // ── Simple field mappings ──────────────────────────────────────────────────
  const validFields = fieldMappings.filter(
    (m) => m.target.trim() && (m.source.trim() || m.fixedValue !== undefined || m.partnerPropKey || (m.globalSetId && m.globalKey))
  );

  const topLevelArrayMappings = arrayMappings.filter((am) => !am.parentArrayId && am.target);

  // Determine top-level key order from outputJson so fields and arrays are
  // interleaved in the same order they appear in the output template.
  const getTopKey = (target: string) => target.split('.')[0];
  let keyOrder: string[] = [];
  if (outputJson) {
    try {
      const parsed = JSON.parse(outputJson);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        keyOrder = Object.keys(parsed);
      }
    } catch { /* ignore */ }
  }

  const emittedFields = new Set<string>();
  const emittedArrayIds = new Set<string>();

  const emitField = (m: FieldMapping) => {
    const targetType = typeMap[m.target];
    const sourceType = m.source ? sourceTypeMap[m.source] : undefined;
    rootLines.push(`  "${m.target}": ${renderFieldValue(m, undefined, valuesSetMap, targetType, sourceType)},`);
    emittedFields.add(m.target);
  };

  const emitArray = (am: ArrayMapping) => {
    rootLines.push(...renderArrayMapping(am, arrayMappings, undefined, 0, valuesSetMap, typeMap, inputJson, sourceTypeMap));
    emittedArrayIds.add(am.id);
  };

  if (keyOrder.length > 0) {
    // Emit in outputJson key order, grouping by top-level key
    for (const key of keyOrder) {
      for (const m of validFields) {
        if (!emittedFields.has(m.target) && getTopKey(m.target) === key) emitField(m);
      }
      for (const am of topLevelArrayMappings) {
        if (!emittedArrayIds.has(am.id) && getTopKey(am.target) === key) emitArray(am);
      }
    }
    // Emit any remaining items not covered by keyOrder
    for (const m of validFields) { if (!emittedFields.has(m.target)) emitField(m); }
    for (const am of topLevelArrayMappings) { if (!emittedArrayIds.has(am.id)) emitArray(am); }
  } else {
    // No outputJson available — original order: fields first, then arrays
    for (const m of validFields) emitField(m);
    for (const am of topLevelArrayMappings) emitArray(am);
  }

  rootLines.push('}');
  return rootLines.join('\n');
}

/** Recursively embed child AMs' fixedItems — skip keys already present inline. */
function enrichFixedItem(
  item: Record<string, unknown>,
  am: ArrayMapping,
  allAMs: ArrayMapping[]
): Record<string, unknown> {
  const children = allAMs.filter((c) => c.parentArrayId === am.id && c.fixedItems?.length && c.target);
  if (children.length === 0) return item;
  const enriched: Record<string, unknown> = { ...item };
  for (const child of children) {
    if (!(child.target in enriched)) {
      enriched[child.target] = child.fixedItems!.map((fi) =>
        enrichFixedItem(fi as Record<string, unknown>, child, allAMs)
      );
    }
  }
  return enriched;
}

/** Recursively check whether any value in a tree contains a {{dynamic}} reference. */
function hasDynamicDeep(v: unknown): boolean {
  if (typeof v === 'string') return /^\{\{.+\}\}$/.test(v);
  if (Array.isArray(v)) return v.some(hasDynamicDeep);
  if (v && typeof v === 'object') return Object.values(v as object).some(hasDynamicDeep);
  return false;
}

/** Coerce a fixed value to the target type for JSON emission. */
function coerceFixedValue(v: unknown, targetType: 'string' | 'number' | 'boolean' | undefined): unknown {
  if (targetType === 'number') {
    if (typeof v === 'boolean') return v ? 1 : 0;  // bool → num: true→1, false→0
    const n = Number(v);
    return isNaN(n) ? v : n;
  }
  if (targetType === 'boolean') {
    if (typeof v === 'boolean') return v;
    if (typeof v === 'number') return v === 0 ? false : v === 1 ? true : null; // num → bool: 0→false, 1→true, else null
    return null; // string → bool: always null
  }
  if (targetType === 'string') {
    if (typeof v === 'boolean') return v ? 'true' : 'false'; // bool → string
    return String(v);
  }
  return v; // unknown target type — keep as-is
}

/** Recursively emit a fixed-item value as Scriban-safe line(s), expanding {{expr}} references. */
function emitFixedValueLines(v: unknown, pad: string, typeMap?: TypeMap, fieldPath?: string, sourceTypeMap?: TypeMap, sourceItemPrefix?: string): string[] {
  if (Array.isArray(v)) {
    if (!v.some(hasDynamicDeep)) return [JSON.stringify(v)];
    const subPad = pad + '  ';
    const result: string[] = ['['];
    for (const item of v) {
      if (item && typeof item === 'object' && !Array.isArray(item)) {
        result.push(`${subPad}{`);
        const kPad = subPad + '  ';
        for (const [k, sv] of Object.entries(item as Record<string, unknown>)) {
          const childPath = fieldPath ? `${fieldPath}.${k}` : k;
          const vLines = emitFixedValueLines(sv, kPad, typeMap, childPath, sourceTypeMap, sourceItemPrefix);
          result.push(`${kPad}"${k}": ${vLines[0]}${vLines.length === 1 ? ',' : ''}`);
          for (let li = 1; li < vLines.length - 1; li++) result.push(vLines[li]);
          if (vLines.length > 1) result.push(`${vLines[vLines.length - 1]},`);
        }
        result.push(`${subPad}},`);
      } else {
        const vLines = emitFixedValueLines(item, subPad, typeMap, fieldPath, sourceTypeMap, sourceItemPrefix);
        result.push(`${subPad}${vLines.join('\n')},`);
      }
    }
    result.push(`${pad}]`);
    return result;
  }
  if (typeof v === 'string') {
    const m = v.match(/^\{\{(.+)\}\}$/);
    if (m) {
      const expr = m[1].trim();
      const targetType = fieldPath ? typeMap?.[fieldPath] : undefined;
      // Resolve source type by looking up the full expression as a dot-path first
      // (fixed-item source values are always root-level paths like "customer.city"),
      // then fall back to alias-stripped last-segment lookup for item-relative paths.
      let exprSourceType: 'string' | 'number' | 'boolean' | undefined;
      if (sourceTypeMap) {
        // 1. Direct root path lookup — covers "customer.city", "order.status", etc.
        exprSourceType = sourceTypeMap[expr];
        // 2. Alias-relative: strip the leading "alias." if present, look up under item prefix
        if (!exprSourceType && sourceItemPrefix) {
          const fieldNameMatch = expr.match(/\.(\w+)$/) ?? expr.match(/^(\w+)$/);
          const fieldName = fieldNameMatch?.[1];
          if (fieldName) {
            exprSourceType = sourceTypeMap[`${sourceItemPrefix}.${fieldName}`] ?? sourceTypeMap[fieldName];
          }
        }
      }
      // Lookup dict expressions ($__e = ...) must not be wrapped with castExpr —
      // the assignment+semicolon syntax is invalid inside a parenthesised cast expression.
      const isLookup = expr.includes('$__e =');
      return targetType && !isLookup
        ? [`{{ ${castExpr(expr, targetType, exprSourceType)} | json }}`]
        : [`{{ ${expr} | json }}`];
    }
    const targetType = fieldPath ? typeMap?.[fieldPath] : undefined;
    return [JSON.stringify(coerceFixedValue(v, targetType))];
  }
  // Non-string primitives (number, boolean) — apply target type coercion if known
  const primitiveTargetType = fieldPath ? typeMap?.[fieldPath] : undefined;
  if (primitiveTargetType !== undefined) return [JSON.stringify(coerceFixedValue(v, primitiveTargetType))];
  return [JSON.stringify(v)];
}

/**
 * Renders a root-array template: the entire output is `[ ... ]` with no wrapping object.
 * Used when ArrayMapping.isRootOutput = true.
 * The loop body and field rendering are identical to renderArrayMapping; only the outer
 * container changes from `"target": [ ... ]` to just `[ ... ]`.
 */
function renderRootArrayMapping(
  am: ArrayMapping,
  allAMs: ArrayMapping[],
  valuesSetMap?: ValuesSetMap,
  typeMap?: TypeMap,
  inputJson?: string,
  sourceTypeMap?: TypeMap
): string {
  const alias = am.alias || 'item';
  const source = am.source;

  // Build the item type-map prefix — for root output we use the source path directly
  // (e.g. "items[*]" when source is "items", or just "[*]" when input is a root array)
  const itemPrefix = source ? `${source}[*]` : '[*]';

  const filterExpr = am.filter
    ? `${alias}.${am.filter.field} ${OPERATOR_MAP[am.filter.operator] ?? am.filter.operator} ${
        typeof am.filter.value === 'number' ? am.filter.value : `"${am.filter.value}"`
      }`
    : '';

  const lines: string[] = ['['];

  // ── Primitive root array (no loop variable needed in the item body) ────────
  if (am.primitiveItems != null) {
    for (const item of am.primitiveItems) {
      const elemType = typeMap?.[itemPrefix];
      if (item.partnerPropKey) {
        lines.push(elemType === 'number' || elemType === 'boolean'
          ? '  null,'
          : `  {{ __partner__?.${item.partnerPropKey} | json }},`);
      } else if (item.globalSetId && item.globalKey) {
        lines.push(elemType === 'number' || elemType === 'boolean'
          ? '  null,'
          : `  {{ __globals__?.${item.globalSetId}["${item.globalKey}"] | json }},`);
      } else if (item.source?.trim()) {
        const path = item.source.trim();
        if (item.transform?.trim()) {
          const expr = item.transform.trim().replace(/\bvalue\b/g, path);
          lines.push(`  {{ ${expr} | json }},`);
        } else {
          lines.push(`  {{ ${path} | json }},`);
        }
      } else if (item.fixedValue !== undefined) {
        lines.push(`  ${JSON.stringify(coerceFixedValue(item.fixedValue, typeMap?.[itemPrefix]))},`);
      } else {
        lines.push('  null,');
      }
    }
    lines.push(']');
    return lines.join('\n');
  }

  // ── Fixed items before the dynamic loop ───────────────────────────────────
  if (am.fixedItems?.length) {
    for (const rawItem of am.fixedItems) {
      const enriched = enrichFixedItem(rawItem as Record<string, unknown>, am, allAMs);
      if (!hasDynamicDeep(enriched)) {
        lines.push(`  ${JSON.stringify(enriched)},`);
      } else {
        lines.push('  {');
        for (const [k, v] of Object.entries(enriched)) {
          const fp = `${itemPrefix}.${k}`;
          const vLines = emitFixedValueLines(v, '    ', typeMap, fp, sourceTypeMap, source ? `${source}[*]` : '');
          lines.push(`    "${k}": ${vLines[0]}${vLines.length === 1 ? ',' : ''}`);
          for (let li = 1; li < vLines.length - 1; li++) lines.push(vLines[li]);
          if (vLines.length > 1) lines.push(`${vLines[vLines.length - 1]},`);
        }
        lines.push('  },');
      }
    }
  }

  if (!source) {
    lines.push(']');
    return lines.join('\n');
  }

  lines.push(`{{- for ${alias} in ${source} -}}`);
  if (am.filter) lines.push(`{{- if ${filterExpr} -}}`);
  lines.push('{');
  for (const m of am.mappings) {
    if (!m.target.trim()) continue;
    // typeMap is built from the root-array output JSON, whose keys are bare field
    // names (no prefix), so look up by m.target directly.
    const targetType = typeMap?.[m.target];
    const sourceType = m.source ? sourceTypeMap?.[`${itemPrefix}.${m.source}`] : undefined;
    lines.push(`  "${m.target}": ${renderFieldValue(m, alias, valuesSetMap, targetType, sourceType)},`);
  }
  // Render child AMs recursively inside this loop body
  const children = allAMs.filter((c) => c.parentArrayId === am.id && c.source && c.target);
  for (const child of children) {
    lines.push(...renderArrayMapping(child, allAMs, alias, 1, valuesSetMap, typeMap, inputJson, sourceTypeMap));
  }
  lines.push('},');
  if (am.filter) lines.push('{{- end -}}');
  lines.push('{{- end -}}');
  lines.push(']');
  return lines.join('\n');
}

/**
 * Recursively render a single ArrayMapping block (and its children) at a given nesting depth.
 * depth=0 → 2-space indent (root-level); depth=1 → 4-space indent (inside a loop), etc.
 */
function renderArrayMapping(
  am: ArrayMapping,
  allAMs: ArrayMapping[],
  outerAlias: string | undefined,
  depth: number,
  valuesSetMap?: ValuesSetMap,
  typeMap?: TypeMap,
  inputJson?: string,
  sourceTypeMap?: TypeMap
): string[] {
  const pad = ' '.repeat(2 * (depth + 1));
  const fieldPad = ' '.repeat(2 * (depth + 2));
  const alias = am.alias || 'item';
  const source = outerAlias ? `${outerAlias}.${am.source}` : am.source;
  const filterExpr = renderFilter(alias, am.filter ?? undefined);

  // Build the full item prefix for typeMap lookups, e.g. "labels[*]" or "orders[*].items[*]".
  // Root-output AMs have target='' and no parent — they contribute no prefix segment so that
  // their children resolve to "lines[*]" rather than "[*].lines[*]".
  function resolvePrefix(id: string): string {
    const m = allAMs.find((a) => a.id === id);
    if (!m) return '';
    if (!m.parentArrayId) return m.target ? `${m.target}[*]` : '';
    const parentPrefix = resolvePrefix(m.parentArrayId);
    return parentPrefix ? `${parentPrefix}.${m.target}[*]` : `${m.target}[*]`;
  }
  const itemPrefix = resolvePrefix(am.id);

  // Builds the full dot-path to this AM's source array (e.g. "orders" or "orders.items")
  const buildFullSrcPath = (id: string): string => {
    const a = allAMs.find((x) => x.id === id);
    if (!a) return '';
    if (!a.parentArrayId) return a.source;
    return `${buildFullSrcPath(a.parentArrayId)}.${a.source}`;
  };
  // Source item prefix for sourceTypeMap lookups (e.g. "order.products[*]" or "orders[*].items[*]").
  // Walk the actual inputJson to only insert [*] at segments that are genuinely arrays,
  // not blindly after every dot — "order.products" should become "order.products[*]",
  // not "order[*].products[*]".
  const srcDotPath = buildFullSrcPath(am.id);
  let sourceArrayItemPrefix = '';
  if (srcDotPath) {
    let walked = false;
    if (inputJson) {
      try {
        const root = JSON.parse(inputJson);
        const segments = srcDotPath.split('.');
        let node: any = root;
        const prefixParts: string[] = [];
        for (const seg of segments) {
          if (node == null) break;
          if (Array.isArray(node)) node = node[0]; // step into first item of enclosing array
          if (typeof node !== 'object' || node == null) break;
          const val = node[seg];
          if (Array.isArray(val)) {
            prefixParts.push(`${seg}[*]`);
            node = val;
          } else {
            prefixParts.push(seg);
            node = val;
          }
        }
        if (prefixParts.length === segments.length) {
          sourceArrayItemPrefix = prefixParts.join('.');
          walked = true;
        }
      } catch { /* ignore */ }
    }
    // Fallback when inputJson is absent or the walk couldn't complete
    if (!walked) {
      sourceArrayItemPrefix = srcDotPath.split('.').join('[*].') + '[*]';
    }
  }

  // Build a set of ALL dot-paths that exist within an array item — used as fallback
  // for old mappings that predate the isRootSource flag.
  const itemPathSet = new Set<string>();
  if (inputJson && am.source) {
    try {
      const root = JSON.parse(inputJson);
      let node: any = root;
      for (const part of buildFullSrcPath(am.id).split('.')) {
        if (node == null) break;
        if (Array.isArray(node)) node = node[0];
        if (typeof node !== 'object' || node == null) break;
        node = node[part];
      }
      if (Array.isArray(node) && node.length > 0 && node[0] && typeof node[0] === 'object') {
        const collectPaths = (obj: any, prefix: string) => {
          if (!obj || typeof obj !== 'object' || Array.isArray(obj)) { if (prefix) itemPathSet.add(prefix); return; }
          for (const [k, v] of Object.entries(obj)) {
            const p = prefix ? `${prefix}.${k}` : k;
            itemPathSet.add(p);
            collectPaths(v, p);
          }
        };
        collectPaths(node[0], '');
      }
    } catch { /* ignore */ }
  }

  const lines: string[] = [];
  lines.push(`${pad}"${am.target}": [`);

  // ── Primitive array (each element mapped individually — no loop) ────────────
  if (am.primitiveItems != null) {
    for (const item of am.primitiveItems) {
      const elemType = typeMap?.[itemPrefix];
      if (item.partnerPropKey) {
        // Partner values are always strings — incompatible with number/boolean targets
        if (elemType === 'number' || elemType === 'boolean') {
          lines.push(`${fieldPad}null,`);
        } else {
          lines.push(`${fieldPad}{{ __partner__?.${item.partnerPropKey} | json }},`);
        }
      } else if (item.globalSetId && item.globalKey) {
        // Global set values are always strings — incompatible with number/boolean targets
        if (elemType === 'number' || elemType === 'boolean') {
          lines.push(`${fieldPad}null,`);
        } else {
          lines.push(`${fieldPad}{{ __globals__?.${item.globalSetId}["${item.globalKey}"] | json }},`);
        }
      } else if (item.source?.trim()) {
        const path = item.source.trim();
        // If the source path resolves to an array in the input JSON, unroll it with a for-loop
        // instead of embedding {{ path | json }} directly (which would produce [[...]])
        const sourceIsArray = (() => {
          if (!inputJson) return false;
          try {
            let node: unknown = JSON.parse(inputJson);
            for (const part of path.split('.')) {
              if (node == null || typeof node !== 'object') return false;
              node = (node as Record<string, unknown>)[part];
            }
            return Array.isArray(node);
          } catch { return false; }
        })();
        if (sourceIsArray) {
          const loopAlias = am.alias || 'item';
          lines.push(`${pad}{{- for ${loopAlias} in ${path} -}}`);
          if (item.transform?.trim()) {
            const expr = item.transform.trim().replace(/\bvalue\b/g, loopAlias);
            lines.push(`${fieldPad}{{ ${expr} | json }},`);
          } else {
            lines.push(`${fieldPad}{{ ${loopAlias} | json }},`);
          }
          lines.push(`${pad}{{- end -}}`);
        } else if (item.transform?.trim()) {
          const expr = item.transform.trim().replace(/\bvalue\b/g, path);
          lines.push(`${fieldPad}{{ ${expr} | json }},`);
        } else {
          lines.push(`${fieldPad}{{ ${path} | json }},`);
        }
      } else if (item.fixedValue !== undefined) {
        const coerced = coerceFixedValue(item.fixedValue, elemType);
        lines.push(`${fieldPad}${JSON.stringify(coerced)},`);
      } else {
        lines.push(`${fieldPad}null,`);
      }
    }
    lines.push(`${pad}],`);
    return lines;
  }

  if (am.fixedItems?.length) {
    for (const rawItem of am.fixedItems) {
      const enriched = enrichFixedItem(rawItem as Record<string, unknown>, am, allAMs);
      if (!hasDynamicDeep(enriched)) {
        // Coerce each top-level field to the correct target type
        const coerced: Record<string, unknown> = {};
        for (const [k, v] of Object.entries(enriched)) {
          const targetType = typeMap?.[`${itemPrefix}.${k}`];
          coerced[k] = targetType !== undefined ? coerceFixedValue(v, targetType) : v;
        }
        lines.push(`${pad}${JSON.stringify(coerced)},`);
      } else {
        lines.push(`${pad}{`);
        for (const [k, v] of Object.entries(enriched)) {
          const fieldPath = `${itemPrefix}.${k}`;
          const vLines = emitFixedValueLines(v, fieldPad, typeMap, fieldPath, sourceTypeMap, sourceArrayItemPrefix);
          lines.push(`${fieldPad}"${k}": ${vLines[0]}${vLines.length === 1 ? ',' : ''}`);
          for (let li = 1; li < vLines.length - 1; li++) lines.push(vLines[li]);
          if (vLines.length > 1) lines.push(`${vLines[vLines.length - 1]},`);
        }
        lines.push(`${pad}},`);
      }
    }
  }
  if (!am.source) {
    // Fixed-items-only array — no loop needed
    lines.push(`${pad}],`);
    return lines;
  }
  lines.push(`${pad}{{- for ${alias} in ${source} -}}`);
  if (am.filter) {
    lines.push(`${pad}{{- if ${filterExpr} -}}`);
  }
  lines.push(`${pad}{`);
  for (const m of am.mappings) {
    if (!m.target.trim()) continue;
    const targetType = typeMap?.[`${itemPrefix}.${m.target}`];
    // isRootSource is set explicitly by the modal when user picks from root fields group.
    // For old mappings without the flag, fall back to checking itemPathSet from inputJson:
    // if the source is not a known item path, it must be a root field.
    const isRoot = m.isRootSource === true ||
      (m.isRootSource === undefined && itemPathSet.size > 0 && Boolean(m.source) && !itemPathSet.has(m.source));
    const sourceType = m.source
      ? isRoot
        ? sourceTypeMap?.[m.source]
        : sourceTypeMap?.[sourceArrayItemPrefix ? `${sourceArrayItemPrefix}.${m.source}` : m.source]
      : undefined;
    lines.push(`${fieldPad}"${m.target}": ${renderFieldValue(m, isRoot ? undefined : alias, valuesSetMap, targetType, sourceType)},`);
  }
  // Render child AMs recursively inside this loop body
  const children = allAMs.filter((c) => c.parentArrayId === am.id && c.source && c.target);
  for (const child of children) {
    lines.push(...renderArrayMapping(child, allAMs, alias, depth + 1, valuesSetMap, typeMap, inputJson, sourceTypeMap));
  }
  lines.push(`${pad}},`);
  if (am.filter) {
    lines.push(`${pad}{{- end -}}`);
  }
  lines.push(`${pad}{{- end -}}`);
  lines.push(`${pad}],`);
  return lines;
}

// ─── Re-exported from scribanParser.ts ───────────────────────────────────────

export type { ParsedFieldMapping, ParsedArrayMapping, ParsedMappings } from './scribanParser';
export { resolveParentArrayIds, parseScriban } from './scribanParser';









