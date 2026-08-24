// ─── Mapping domain types ──────────────────────────────────────────────────────

/** Legacy adapter-property shape. The prototype uses Record<string,string> and
 *  adapts to/from this only at the persistence boundary (see loadFromProps / useSave). */
export interface KeyValuePair {
  key: string;
  value: string;
}

export type FilterOperator = '==' | '!=' | '>' | '>=' | '<' | '<=';

export interface FilterCondition {
  field: string;
  operator: FilterOperator;
  value: string | number;
}

export interface LookupEntry {
  from: string;
  to: string;
}

export interface LookupDictionary {
  entries: LookupEntry[];
  /** What to output when the source value isn't in the dictionary.
   *  null = output null | custom = use fallbackValue */
  fallback: 'null' | 'custom';
  fallbackValue?: string;
}

export interface FieldMapping {
  id: string;
  source: string;
  target: string;
  transform?: string;
  fixedValue?: string;
  /** Legacy global values-set reference — still rendered for backward compat. */
  valuesSetId?: string;
  /** Inline dictionary lookup — takes precedence over valuesSetId. */
  lookupDictionary?: LookupDictionary;
  /** True when source points to a root-level field (not an item field of the loop array). */
  isRootSource?: boolean;
  /** Partner adapter property key — mutually exclusive with source and fixedValue.
   *  Generates {{ __partner__.<key> | json }} in the Scriban template. */
  partnerPropKey?: string;
  /** Global adapter values set reference — mutually exclusive with other modes.
   *  Generates {{ __globals__?.SETID?.KEY | json }} in the Scriban template. */
  globalSetId?: string;
  globalKey?: string;
}

/** One element in a primitive array mapping (string/number target array). */
export interface PrimitiveArrayItem {
  source?: string;
  fixedValue?: string;
  transform?: string;
  partnerPropKey?: string;
  globalSetId?: string;
  globalKey?: string;
}

export interface ArrayMapping {
  id: string;
  source: string;
  target: string;
  alias: string;
  filter?: FilterCondition;
  mappings: FieldMapping[];
  fixedItems?: Record<string, unknown>[];
  /** Primitive (string/number) array: each index mapped to a source field or fixed value. */
  primitiveItems?: PrimitiveArrayItem[];
  /** ID of the parent ArrayMapping. undefined/null means top-level. */
  parentArrayId?: string;
  /**
   * When true this ArrayMapping owns the root `[...]` output instead of a
   * named field.  Only valid on top-level AMs (no parentArrayId).
   * Generator emits `[ for … ]` instead of `"target": [ for … ]`.
   */
  isRootOutput?: boolean;
}

export type ValidationError = {
  type: 'error' | 'warning';
  message: string;
  path?: string;
};

export type EditorMode = 'visual' | 'canvas' | 'manual';

/** The active value-source mode for a single field mapping row. */
export type MappingMode = 'source' | 'fixed' | 'partner' | 'global';

export const NATIVE_JSON_MAPPER_ID = 'NativeJSONMapper';

let _id = 0;
export const genId = () => `m-${++_id}-${Date.now()}`;
