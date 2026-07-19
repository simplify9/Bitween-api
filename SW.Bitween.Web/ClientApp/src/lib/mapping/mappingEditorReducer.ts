import { produce } from 'immer';
import { KeyValuePair } from './types';
import { parseScriban, resolveParentArrayIds } from './scribanGenerator';
import { ArrayMapping, FieldMapping, ValidationError, genId } from './types';
import { CoreState, MappingEditorAction, MappingEditorState } from './mappingEditorActions';

// ─── Helpers ──────────────────────────────────────────────────────────────────

export function snapshot(state: MappingEditorState): CoreState {
  return {
    fieldMappings: state.fieldMappings.map((m) => ({ ...m })),
    arrayMappings: state.arrayMappings.map((am) => ({
      ...am,
      mappings: am.mappings.map((m) => ({ ...m })),
      fixedItems: am.fixedItems ? am.fixedItems.map((fi) => ({ ...fi })) : undefined,
      primitiveItems: am.primitiveItems ? am.primitiveItems.map((pi) => ({ ...pi })) : undefined,
    })),
  };
}

export function flatPathsFromObj(obj: any, prefix = ''): string[] {
  if (typeof obj !== 'object' || obj === null) return prefix ? [prefix] : [];
  if (Array.isArray(obj)) {
    if (obj.length === 0 || typeof obj[0] !== 'object' || obj[0] === null) return prefix ? [prefix] : [];
    return flatPathsFromObj(obj[0], `${prefix}[*]`);
  }
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k;
    return flatPathsFromObj(v, path);
  });
}

export function loadFromProps(props: KeyValuePair[]): CoreState {
  const rulesEntry = props.find((p) => p.key === 'Rules');
  const scribanEntry = props.find((p) => p.key === 'ScribanTemplate');
  const arrayEntry = props.find((p) => p.key === 'ArrayRules');
  const fieldMappings: FieldMapping[] = [];
  const arrayMappings: ArrayMapping[] = [];

  if (rulesEntry?.value) {
    try {
      const parsed = JSON.parse(rulesEntry.value);
      if (Array.isArray(parsed)) {
        parsed.forEach((r: any, i: number) => {
          fieldMappings.push({
            id: `loaded-${i}`,
            source: r.sourcePath ?? r.SourcePath ?? '',
            target: r.outputField ?? r.OutputField ?? '',
            transform: r.transform ?? r.Transform ?? undefined,
            fixedValue: r.fixedValue ?? r.FixedValue ?? undefined,
            valuesSetId: r.valuesSetId ?? r.ValuesSetId ?? undefined,
          });
        });
      }
    } catch {}
  } else if (scribanEntry?.value && scribanEntry.value !== '{}') {
    try {
      const parsed = parseScriban(scribanEntry.value);
      parsed.fieldMappings.forEach((m, i) => {
        fieldMappings.push({ id: `loaded-${i}`, ...m });
      });
      if (!arrayEntry?.value) {
        const rawAMs = parsed.arrayMappings.map((am, i) => ({
          id: `arr-loaded-${i}`,
          ...am,
          mappings: am.mappings.map((m, j) => ({ id: `arr-loaded-${i}-${j}`, ...m })),
        }));
        resolveParentArrayIds(rawAMs).forEach((am) => arrayMappings.push(am as ArrayMapping));
      }
    } catch {}
  }

  if (arrayEntry?.value) {
    try {
      const parsed = JSON.parse(arrayEntry.value);
      if (Array.isArray(parsed)) {
        parsed.forEach((am: any, i: number) => {
          arrayMappings.push({
            id: am.id ?? `arr-loaded-${i}`,
            source: am.source ?? '',
            target: am.target ?? '',
            alias: am.alias ?? 'item',
            filter: am.filter,
            parentArrayId: am.parentArrayId ?? undefined,
            isRootOutput: am.isRootOutput ?? undefined,
            mappings: (am.mappings ?? []).map((m: any, j: number) => ({
              id: `arr-loaded-${i}-${j}`,
              source: m.source ?? '',
              target: m.target ?? '',
              transform: m.transform,
              fixedValue: m.fixedValue,
              valuesSetId: m.valuesSetId ?? undefined,
              isRootSource: m.isRootSource ?? undefined,
              lookupDictionary: m.lookupDictionary ?? undefined,
              partnerPropKey: m.partnerPropKey ?? undefined,
              globalSetId: m.globalSetId ?? undefined,
              globalKey: m.globalKey ?? undefined,
            })),
            fixedItems: Array.isArray(am.fixedItems) ? am.fixedItems : undefined,
            primitiveItems: Array.isArray(am.primitiveItems) ? am.primitiveItems : undefined,
          });
        });
      }
    } catch {}
  }

  return { fieldMappings, arrayMappings };
}

// ─── Initial state ────────────────────────────────────────────────────────────

export const initialMappingEditorState: MappingEditorState = {
  inputJson: '',
  outputJson: '',
  fieldMappings: [],
  arrayMappings: [],
  mode: 'visual',
  manualTemplate: '',
  isManualDirty: false,
  selectedMappingId: null,
  hoveredPath: null,
  searchInput: '',
  searchOutput: '',
  collapsedNodes: [],
  editingArrayId: null,
  newArrayPresetTarget: '',
  newArrayParentId: null,
  newArrayIsRootOutput: false,
  past: [],
  future: [],
  validationErrors: [],
  showValidation: false,
  showPreview: false,
  subscriptionId: null,
  mapperId: null,
  selectedPartnerId: null,
  partnerAdapterProperties: {},
};

// ─── Reducer ──────────────────────────────────────────────────────────────────

export function mappingEditorReducer(
  state: MappingEditorState,
  action: MappingEditorAction
): MappingEditorState {
  return produce(state, (draft) => {
    switch (action.type) {
      case 'LOAD_CONTEXT': {
        const { subscriptionId, mapperId, mapperProperties } = action.payload;
        draft.subscriptionId = subscriptionId;
        draft.mapperId = mapperId ?? null;
        draft.fieldMappings = [];
        draft.arrayMappings = [];
        draft.inputJson = '';
        draft.outputJson = '';
        if (mapperProperties && mapperProperties.length > 0) {
          const core = loadFromProps(mapperProperties);
          draft.fieldMappings = core.fieldMappings;
          draft.arrayMappings = core.arrayMappings;
          const sjEntry = mapperProperties.find((p) => p.key === 'SourceJson');
          const tjEntry = mapperProperties.find((p) => p.key === 'TargetJson');
          if (sjEntry?.value) draft.inputJson = sjEntry.value;
          if (tjEntry?.value) draft.outputJson = tjEntry.value;
          const pidEntry = mapperProperties.find((p) => p.key === 'PartnerId');
          if (pidEntry?.value) draft.selectedPartnerId = Number(pidEntry.value);
        }
        draft.past = [];
        draft.future = [];
        draft.selectedMappingId = null;
        draft.mode = 'visual';
        draft.isManualDirty = false;
        if (!draft.selectedPartnerId) draft.selectedPartnerId = null;
        draft.partnerAdapterProperties = {};
        break;
      }
      case 'SET_INPUT_JSON':
        draft.inputJson = action.payload;
        break;
      case 'SET_OUTPUT_JSON':
        draft.outputJson = action.payload;
        break;
      case 'SET_MODE':
        draft.mode = action.payload;
        break;
      case 'SET_MANUAL_TEMPLATE':
        draft.manualTemplate = action.payload;
        draft.isManualDirty = true;
        break;
      case 'SYNC_MANUAL_TEMPLATE':
        draft.manualTemplate = action.payload;
        draft.isManualDirty = false;
        break;
      case 'CLEAR_MANUAL_DIRTY':
        draft.isManualDirty = false;
        break;
      case 'ADD_FIELD_MAPPING':
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        draft.fieldMappings.push({ id: genId(), ...action.payload });
        break;
      case 'REMOVE_FIELD_MAPPING':
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        draft.fieldMappings = draft.fieldMappings.filter((m) => m.id !== action.payload);
        if (draft.selectedMappingId === action.payload) draft.selectedMappingId = null;
        break;
      case 'UPDATE_FIELD_MAPPING': {
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        const idx = draft.fieldMappings.findIndex((m) => m.id === action.payload.id);
        if (idx !== -1) draft.fieldMappings[idx] = { ...draft.fieldMappings[idx], ...action.payload };
        break;
      }
      case 'SET_FIELD_MAPPINGS':
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        draft.fieldMappings = action.payload;
        break;
      case 'SET_ARRAY_MAPPINGS':
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        draft.arrayMappings = action.payload;
        break;
      case 'ADD_ARRAY_MAPPING':
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        draft.arrayMappings.push({ id: genId(), ...action.payload });
        break;
      case 'REMOVE_ARRAY_MAPPING':
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        draft.arrayMappings = draft.arrayMappings.filter((m) => m.id !== action.payload);
        break;
      case 'UPDATE_ARRAY_MAPPING': {
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        const idx = draft.arrayMappings.findIndex((m) => m.id === action.payload.id);
        if (idx !== -1) draft.arrayMappings[idx] = { ...draft.arrayMappings[idx], ...action.payload };
        break;
      }
      case 'SELECT_MAPPING':
        draft.selectedMappingId = action.payload;
        break;
      case 'SET_HOVERED_PATH':
        draft.hoveredPath = action.payload;
        break;
      case 'SET_SEARCH_INPUT':
        draft.searchInput = action.payload;
        break;
      case 'SET_SEARCH_OUTPUT':
        draft.searchOutput = action.payload;
        break;
      case 'TOGGLE_NODE_COLLAPSED': {
        const idx = draft.collapsedNodes.indexOf(action.payload);
        if (idx === -1) draft.collapsedNodes.push(action.payload);
        else draft.collapsedNodes.splice(idx, 1);
        break;
      }
      case 'OPEN_ARRAY_MODAL':
        draft.editingArrayId = action.payload.id;
        draft.newArrayPresetTarget = action.payload.presetTarget ?? '';
        draft.newArrayParentId = action.payload.parentArrayId ?? null;
        draft.newArrayIsRootOutput = action.payload.isRootOutput ?? false;
        break;
      case 'TOGGLE_PREVIEW':
        draft.showPreview = !draft.showPreview;
        break;
      case 'SET_VALIDATION_ERRORS':
        draft.validationErrors = action.payload;
        draft.showValidation = action.payload.length > 0;
        break;
      case 'TOGGLE_VALIDATION':
        draft.showValidation = !draft.showValidation;
        break;
      case 'SET_SELECTED_PARTNER': {
        draft.selectedPartnerId = action.payload.partnerId;
        draft.partnerAdapterProperties = action.payload.adapterProperties;
        // Do not clear any partnerPropKey mappings — user manages their own mappings.
        break;
      }
      case 'UNDO': {
        if (state.past.length === 0) break;
        const prev = state.past[state.past.length - 1];
        draft.future = [snapshot(state), ...state.future.slice(0, 49)];
        draft.past = state.past.slice(0, -1);
        draft.fieldMappings = prev.fieldMappings;
        draft.arrayMappings = prev.arrayMappings;
        break;
      }
      case 'REDO': {
        if (state.future.length === 0) break;
        const next = state.future[0];
        draft.past = [...state.past.slice(-49), snapshot(state)];
        draft.future = state.future.slice(1);
        draft.fieldMappings = next.fieldMappings;
        draft.arrayMappings = next.arrayMappings;
        break;
      }
      case 'CLEAR_ALL':
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        draft.fieldMappings = [];
        draft.arrayMappings = [];
        break;
      case 'AUTO_MATCH': {
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        const norm = (s: string) =>
          (s.split('.').pop() ?? s).toLowerCase().replace(/[_\-\[\]\*]/g, '');
        let inputPaths: string[] = [];
        let outputPaths: string[] = [];
        try { inputPaths = flatPathsFromObj(JSON.parse(state.inputJson)); } catch {}
        try { outputPaths = flatPathsFromObj(JSON.parse(state.outputJson)); } catch {}

        const arrayTargets = new Set(state.arrayMappings.map((am) => am.target));
        const targetsToProcess = outputPaths.length > 0
          ? outputPaths.filter((p) => {
              if (p.includes('[*]')) return false;
              const parts = p.split('.');
              return !parts.some((_, i) => arrayTargets.has(parts.slice(0, i + 1).join('.')));
            })
          : state.fieldMappings.map((m) => m.target);

        const existingByTarget = new Map(state.fieldMappings.map((m) => [m.target, m]));
        const result: FieldMapping[] = [];

        for (const target of targetsToProcess) {
          const existing = existingByTarget.get(target);
          if (existing?.source || existing?.fixedValue !== undefined || existing?.valuesSetId) {
            result.push(existing);
            continue;
          }
          const match = inputPaths.find((p) => norm(p) === norm(target));
          if (existing) {
            result.push(
              match
                ? { ...existing, source: match, fixedValue: undefined, valuesSetId: undefined, transform: undefined }
                : existing
            );
          } else if (match) {
            result.push({ id: genId(), source: match, target });
          }
        }

        for (const m of state.fieldMappings) {
          if (!result.find((r) => r.target === m.target)) result.push(m);
        }

        draft.fieldMappings = result;
        break;
      }
      case 'GENERATE_FROM_TARGET_JSON': {
        draft.past = [...draft.past.slice(-49), snapshot(state)];
        draft.future = [];
        try {
          const target = JSON.parse(state.outputJson);
          const paths = flatPathsFromObj(target);
          draft.fieldMappings = paths.map((p, i) => ({
            id: `gen-${i}-${Date.now()}`,
            target: p,
            source: '',
            isNullValue: true,
          }));
        } catch {}
        break;
      }
    }
  });
}
