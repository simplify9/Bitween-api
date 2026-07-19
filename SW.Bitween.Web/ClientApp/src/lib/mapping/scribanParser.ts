import { ArrayMapping, FieldMapping, FilterOperator, LookupDictionary, LookupEntry } from './types';

export type ParsedFieldMapping = Omit<FieldMapping, 'id'>;
export type ParsedArrayMapping = {
  source: string;
  target: string;
  alias: string;
  filter?: ArrayMapping['filter'];
  mappings: ParsedFieldMapping[];
  /** When true, this AM owns the root `[...]` output — no wrapping object. */
  isRootOutput?: boolean;
  /** Target of the parent AM — used to resolve parentArrayId after IDs are assigned. */
  parentTarget?: string;
};

export interface ParsedMappings {
  fieldMappings: ParsedFieldMapping[];
  arrayMappings: ParsedArrayMapping[];
  warnings: string[];
}

/**
 * Second-pass helper: resolve `parentTarget` string links to real `parentArrayId` references.
 * Call after assigning IDs to the first-pass array so parent lookup works correctly.
 */
export function resolveParentArrayIds<T extends { id: string; target: string; parentTarget?: string }>(
  rawAMs: T[]
): Omit<T, 'parentTarget'>[] {
  return rawAMs.map((am) => {
    const { parentTarget, ...rest } = am;
    if (!parentTarget) return rest as Omit<T, 'parentTarget'>;
    const parent = rawAMs.find((p) => p.target === parentTarget);
    return { ...rest, parentArrayId: parent?.id } as Omit<T, 'parentTarget'>;
  });
}

/** Strip `| json` suffix from a Scriban expression */
function stripJsonPipe(expr: string): string {
  return expr.replace(/\s*\|\s*json\s*$/, '').trim();
}

/** Parse a Scriban expression body into source/transform/valuesSetId/isRootSource/lookupDictionary */
function parseExpr(
  raw: string,
  valuesSetId: string | undefined,
  alias?: string,
): Pick<ParsedFieldMapping, 'source' | 'transform' | 'valuesSetId' | 'isRootSource' | 'lookupDictionary' | 'partnerPropKey' | 'globalSetId' | 'globalKey'> {
  const expr = stripJsonPipe(raw.trim());

  // Partner adapter property — {{ __partner__.propkey | json }} or {{ __partner__?.propkey | json }}
  const partnerMatch = expr.match(/^__partner__\??\.(\w+)$/);
  if (partnerMatch) {
    return { source: '', partnerPropKey: partnerMatch[1] };
  }

  // Global adapter values set — {{ __globals__?.SETID["KEY"] | json }} or {{ __globals__?.SETID?["KEY"] | json }}
  const globalMatch = expr.match(/^__globals__\??\.([\w]+)\??(?:\.(\w+)|\["([^"]+)"\])$/);
  if (globalMatch) {
    return { source: '', globalSetId: globalMatch[1], globalKey: globalMatch[2] ?? globalMatch[3] };
  }

  // Dictionary lookup — $__e = { ... }; ...
  if (valuesSetId || expr.startsWith('$__e')) {
    if (!valuesSetId) {
      // New inline dictionary — parse entries and fallback from the expression
      const entriesBlockMatch = expr.match(/\$__e\s*=\s*\{([^}]*)\}/);
      const pathMatch = expr.match(/\$__e\[([\w.]+)\]/);
      if (entriesBlockMatch && pathMatch) {
        const entries: LookupEntry[] = [];
        const entryRx = /"([^"]+)":\s*(?:"([^"]*)"|(\btrue\b|\bfalse\b|-?[\d.]+))/g;
        let em: RegExpExecArray | null;
        while ((em = entryRx.exec(entriesBlockMatch[1])) !== null) {
          entries.push({ from: em[1], to: em[2] !== undefined ? em[2] : em[3] });
        }
        const rawPath = pathMatch[1];
        const isRoot = Boolean(alias && !rawPath.startsWith(`${alias}.`));
        const source = alias && rawPath.startsWith(`${alias}.`)
          ? rawPath.slice(alias.length + 1)
          : rawPath;
        // Detect fallback: look for ?? clause at end
        const fallbackMatch = expr.match(/\?\?\s*(.+?)\s*\)\s*(?:\|.*)?$/);
        let fallback: LookupDictionary['fallback'] = 'null';
        let fallbackValue: string | undefined;
        if (fallbackMatch) {
          const fb = fallbackMatch[1].trim();
          if (/^["'].*["']$/.test(fb)) {
            fallback = 'custom';
            fallbackValue = fb.slice(1, -1);
          }
        }
        const lookupDictionary: LookupDictionary = {
          entries,
          fallback,
          ...(fallbackValue !== undefined ? { fallbackValue } : {}),
        };
        return { source, lookupDictionary, ...(isRoot ? { isRootSource: true } : {}) };
      }
    }
    // Old valuesSetId path — backward compat
    const fallback = expr.match(/\?\?\s*([\w.]+)\s*\)/);
    const src = fallback ? fallback[1] : expr;
    const isRoot = Boolean(alias && !src.startsWith(`${alias}.`));
    const source = isRoot ? src : (alias ? src.slice(alias.length + 1) : src);
    return { source, valuesSetId, ...(isRoot ? { isRootSource: true } : {}) };
  }

  // Helper: strip alias prefix and detect root field
  const stripAlias = (src: string): { source: string; isRootSource?: true } => {
    if (alias && src.startsWith(`${alias}.`)) return { source: src.slice(alias.length + 1) };
    if (alias && src !== alias) return { source: src, isRootSource: true };
    return { source: src };
  };

  // castExpr patterns — strip back to plain source so round-trip doesn't produce transforms
  const stringCast = expr.match(/^\(""\s*\+\s*([\w.]+)\)$/);
  if (stringCast) return stripAlias(stringCast[1]);

  const numberCast = expr.match(/^\(([\w.]+)\s*\|\s*to_float\)$/);
  if (numberCast) return stripAlias(numberCast[1]);

  const boolCast = expr.match(/^\(([\w.]+)\s*!=\s*null/);
  if (boolCast) return stripAlias(boolCast[1]);

  if (/^[\w.]+$/.test(expr)) return stripAlias(expr);

  const transformExpr = alias
    ? expr.replace(new RegExp(`\\b${alias}\\.(\\w+)`, 'g'), 'value')
    : expr;
  const srcMatch = expr.match(/^([\w.]+)\s*[^\w.]/);
  const rawSrc = srcMatch ? srcMatch[1] : '';
  const { source, isRootSource } = stripAlias(rawSrc);
  return { source, transform: transformExpr, ...(isRootSource ? { isRootSource } : {}) };
}

/** Parse the body of a for-loop block recursively, collecting field mappings and any nested array blocks. */
function parseForBody(
  lines: string[],
  startI: number,
  alias: string,
  outerTarget: string,
  allArrayMappings: ParsedArrayMapping[]
): { result: { filter?: ArrayMapping['filter']; mappings: ParsedFieldMapping[] }; endI: number } {
  const mappings: ParsedFieldMapping[] = [];
  let filter: ArrayMapping['filter'] | undefined;
  let i = startI;

  while (i < lines.length) {
    const line = lines[i].trim();

    if (line.match(/\{\{-?\s*end\s*-?\}\}/)) { i++; break; }

    const nestedArrOpen = line.match(/"([\w.]+)"\s*:\s*\[/);
    if (nestedArrOpen) {
      const childTarget = nestedArrOpen[1];
      const nextLine = lines[i + 1]?.trim();
      const childForMatch = nextLine?.match(/\{\{-?\s*for\s+(\w+)\s+in\s+([\w.[\]*]+)\s*-?\}\}/);
      if (childForMatch) {
        const childAlias = childForMatch[1];
        const childFullSource = childForMatch[2];
        const childSource = childFullSource.startsWith(`${alias}.`)
          ? childFullSource.slice(alias.length + 1)
          : childFullSource;
        i += 2;
        const { result: childResult, endI } = parseForBody(lines, i, childAlias, childTarget, allArrayMappings);
        i = endI;
        while (i < lines.length && !/^],?$/.test(lines[i].trim())) i++;
        if (i < lines.length) i++;
        allArrayMappings.push({
          source: childSource,
          target: childTarget,
          alias: childAlias,
          filter: childResult.filter,
          mappings: childResult.mappings,
          parentTarget: outerTarget,
        });
        continue;
      }
    }

    const ifMatch = line.match(/\{\{-?\s*if\s+(\w+)\.([\w.]+)\s*([!=<>]+)\s*(.+?)\s*-?\}\}/);
    if (ifMatch) {
      filter = {
        field: ifMatch[2],
        operator: ifMatch[3].trim() as FilterOperator,
        value: ifMatch[4].trim().replace(/^"|"$/g, ''),
      };
      i++; continue;
    }

    const fieldMatch = line.match(/"([\w.]+)"\s*:\s*(.*),?$/);
    if (fieldMatch) {
      const tgt = fieldMatch[1];
      const rawVal = fieldMatch[2].replace(/,$/, '').trim();
      const enumAnn = rawVal.match(/\{\{#\s*enum:([\w-]+)\s*#\}\}/);
      const vsId = enumAnn ? enumAnn[1] : undefined;
      const cleanVal = rawVal.replace(/\{\{#\s*enum:[\w-]+\s*#\}\}/, '').trim();
      const exprMatch = cleanVal.match(/\{\{-?\s*([\s\S]+?)\s*-?\}\}/);
      if (exprMatch) {
        // @ts-expect-error verbatim from legacy Bitween-UI: `source: ''` is an intentional
        // default that the parseExpr spread overwrites; the host tsconfig flags this (TS2783).
        mappings.push({ target: tgt, source: '', ...parseExpr(exprMatch[1], vsId, alias) });
      } else if (!cleanVal.includes('{{') && cleanVal !== 'null' && cleanVal !== '[') {
        mappings.push({ source: '', target: tgt, fixedValue: cleanVal.replace(/^"|"$/g, '') });
      }
    }

    i++;
  }

  return { result: { filter, mappings }, endI: i };
}

export function parseScriban(template: string): ParsedMappings {
  const warnings: string[] = [];
  const fieldMappings: ParsedFieldMapping[] = [];
  const arrayMappings: ParsedArrayMapping[] = [];

  const lines = template.split('\n');
  let i = 0;

  // ── Root-array template detection ────────────────────────────────────────
  // A root-array template starts with `[` (ignoring whitespace) and contains
  // a single top-level `for` loop whose body produces array items.
  // We detect this upfront and parse it as a single AM with isRootOutput=true.
  const firstContentLine = lines.find((l) => l.trim() !== '');
  if (firstContentLine?.trim() === '[') {
    // Find the for-loop line
    const forLineIdx = lines.findIndex((l) => /\{\{-?\s*for\s+/.test(l));
    if (forLineIdx !== -1) {
      const forMatch = lines[forLineIdx].trim().match(/\{\{-?\s*for\s+(\w+)\s+in\s+([\w.[\]*]+)\s*-?\}\}/);
      if (forMatch) {
        const alias = forMatch[1];
        const source = forMatch[2];
        const { result, endI } = parseForBody(lines, forLineIdx + 1, alias, '', arrayMappings);
        // Consume any filter line that may appear before the body
        arrayMappings.push({
          source,
          target: '',
          alias,
          isRootOutput: true,
          filter: result.filter,
          mappings: result.mappings,
        });
        // Any lines after {{- end -}} ] are ignored (should be just `]`)
        void endI;
        return { fieldMappings, arrayMappings, warnings };
      }
    }
    // If no for loop found, return empty (fixed-only root array — not yet supported in visual editor)
    warnings.push('Root-array template has no for-loop — cannot parse back to visual mappings');
    return { fieldMappings, arrayMappings, warnings };
  }

  // Track nesting depth so we only create fieldMappings for lines directly
  // inside the root object (depth === 1).  Lines inside fixed-item literals
  // (e.g. `"lineOrders": [ { "ref": ... } ]`) sit at depth >= 3 and must not
  // be mistaken for top-level field mappings.
  let depth = 0;

  while (i < lines.length) {
    const line = lines[i].trim();

    // Update depth for this line, ignoring brackets inside {{ }} expressions.
    const lineForDepth = line.replace(/\{\{[\s\S]*?\}\}/g, '');
    for (const ch of lineForDepth) {
      if (ch === '{' || ch === '[') depth++;
      else if (ch === '}' || ch === ']') depth--;
    }

    const forMatch = line.match(/\{\{-?\s*for\s+(\w+)\s+in\s+([\w.[\]*]+)\s*-?\}\}/);
    if (forMatch) {
      const alias = forMatch[1];
      const source = forMatch[2];
      let target = '';

      for (let bi = i - 1; bi >= Math.max(0, i - 30); bi--) {
        const prevLine = lines[bi].trim();
        const tMatch = prevLine.match(/"([\w.]+)"\s*:\s*\[/);
        if (tMatch) { target = tMatch[1]; break; }
        if (prevLine.startsWith('"') && prevLine.includes(':') && !prevLine.startsWith('{')) break;
      }

      i++;
      const { result, endI } = parseForBody(lines, i, alias, target, arrayMappings);
      i = endI;

      if (source && target) {
        arrayMappings.push({ source, target, alias, filter: result.filter, mappings: result.mappings });
      } else {
        warnings.push(`Could not determine target array for "for ${alias} in ${source}"`);
      }
      continue;
    }

    // Only parse as a top-level field mapping when directly inside the root object.
    // depth === 1 means we've seen exactly one opening `{` (the root) with no unclosed `[`.
    const simpleMatch = line.match(/"([\w.]+)"\s*:\s*(.*),?$/);
    if (simpleMatch && depth === 1 && !line.includes('{{- for') && !line.startsWith('[') && !line.startsWith(']') && !line.startsWith('{')) {
      const target = simpleMatch[1];
      const valueRaw = simpleMatch[2].replace(/,$/, '').trim();

      const enumAnnotation = valueRaw.match(/\{\{#\s*enum:([\w-]+)\s*#\}\}/);
      const valuesSetId = enumAnnotation ? enumAnnotation[1] : undefined;
      const valueClean = valueRaw.replace(/\{\{#\s*enum:[\w-]+\s*#\}\}/, '').trim();

      const exprMatch = valueClean.match(/\{\{-?\s*([\s\S]+?)\s*-?\}\}/);
      if (exprMatch) {
        const parsed = parseExpr(exprMatch[1], valuesSetId);
        // @ts-expect-error verbatim from legacy Bitween-UI: `source: ''` is an intentional
        // default that the parsed spread overwrites; the host tsconfig flags this (TS2783).
        fieldMappings.push({ target, source: '', ...parsed });
      } else if (!valueClean.includes('{{') && valueClean !== 'null' && !valueClean.startsWith('[')) {
        fieldMappings.push({ source: '', target, fixedValue: valueClean.replace(/^"|"$/g, '') });
      } else if (valueClean.includes('{{')) {
        warnings.push(`Could not parse: ${line}`);
      }
    }

    i++;
  }

  return { fieldMappings, arrayMappings, warnings };
}
