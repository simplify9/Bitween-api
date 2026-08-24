import { KeyValuePair } from './types';
import { ArrayMapping, EditorMode, FieldMapping, ValidationError } from './types';

// ─── State ────────────────────────────────────────────────────────────────────

export interface CoreState {
  fieldMappings: FieldMapping[];
  arrayMappings: ArrayMapping[];
}

export interface MappingEditorState extends CoreState {
  inputJson: string;
  outputJson: string;
  mode: EditorMode;
  manualTemplate: string;
  isManualDirty: boolean;
  selectedMappingId: string | null;
  hoveredPath: string | null;
  searchInput: string;
  searchOutput: string;
  collapsedNodes: string[];
  editingArrayId: string | null;
  newArrayPresetTarget: string;
  newArrayParentId: string | null;
  newArrayIsRootOutput: boolean;
  past: CoreState[];
  future: CoreState[];
  validationErrors: ValidationError[];
  showValidation: boolean;
  showPreview: boolean;
  subscriptionId: number | null;
  mapperId: string | null;
  selectedPartnerId: number | null;
  partnerAdapterProperties: Record<string, string>;
}

// ─── Action type union ────────────────────────────────────────────────────────

export type MappingEditorAction =
  | { type: 'LOAD_CONTEXT'; payload: { subscriptionId: number; mapperId?: string | null; mapperProperties?: KeyValuePair[] } }
  | { type: 'SET_INPUT_JSON'; payload: string }
  | { type: 'SET_OUTPUT_JSON'; payload: string }
  | { type: 'SET_MODE'; payload: EditorMode }
  | { type: 'SET_MANUAL_TEMPLATE'; payload: string }
  | { type: 'SYNC_MANUAL_TEMPLATE'; payload: string }
  | { type: 'CLEAR_MANUAL_DIRTY' }
  | { type: 'ADD_FIELD_MAPPING'; payload: Omit<FieldMapping, 'id'> }
  | { type: 'REMOVE_FIELD_MAPPING'; payload: string }
  | { type: 'UPDATE_FIELD_MAPPING'; payload: Partial<FieldMapping> & { id: string } }
  | { type: 'SET_FIELD_MAPPINGS'; payload: FieldMapping[] }
  | { type: 'SET_ARRAY_MAPPINGS'; payload: ArrayMapping[] }
  | { type: 'ADD_ARRAY_MAPPING'; payload: Omit<ArrayMapping, 'id'> }
  | { type: 'REMOVE_ARRAY_MAPPING'; payload: string }
  | { type: 'UPDATE_ARRAY_MAPPING'; payload: Partial<ArrayMapping> & { id: string } }
  | { type: 'SELECT_MAPPING'; payload: string | null }
  | { type: 'SET_HOVERED_PATH'; payload: string | null }
  | { type: 'SET_SEARCH_INPUT'; payload: string }
  | { type: 'SET_SEARCH_OUTPUT'; payload: string }
  | { type: 'TOGGLE_NODE_COLLAPSED'; payload: string }
  | { type: 'OPEN_ARRAY_MODAL'; payload: { id: string | null; presetTarget?: string; parentArrayId?: string | null; isRootOutput?: boolean } }
  | { type: 'TOGGLE_PREVIEW' }
  | { type: 'SET_VALIDATION_ERRORS'; payload: ValidationError[] }
  | { type: 'TOGGLE_VALIDATION' }
  | { type: 'UNDO' }
  | { type: 'REDO' }
  | { type: 'CLEAR_ALL' }
  | { type: 'AUTO_MATCH' }
  | { type: 'GENERATE_FROM_TARGET_JSON' }
  | { type: 'SET_SELECTED_PARTNER'; payload: { partnerId: number | null; adapterProperties: Record<string, string> } };

// ─── Action creators ──────────────────────────────────────────────────────────

export const loadEditorContext = (p: { subscriptionId: number; mapperId?: string | null; mapperProperties?: KeyValuePair[] }): MappingEditorAction => ({ type: 'LOAD_CONTEXT', payload: p });
export const setInputJson = (p: string): MappingEditorAction => ({ type: 'SET_INPUT_JSON', payload: p });
export const setOutputJson = (p: string): MappingEditorAction => ({ type: 'SET_OUTPUT_JSON', payload: p });
export const setMode = (p: EditorMode): MappingEditorAction => ({ type: 'SET_MODE', payload: p });
export const setManualTemplate = (p: string): MappingEditorAction => ({ type: 'SET_MANUAL_TEMPLATE', payload: p });
export const syncManualTemplate = (p: string): MappingEditorAction => ({ type: 'SYNC_MANUAL_TEMPLATE', payload: p });
export const clearManualDirty = (): MappingEditorAction => ({ type: 'CLEAR_MANUAL_DIRTY' });
export const addFieldMapping = (p: Omit<FieldMapping, 'id'>): MappingEditorAction => ({ type: 'ADD_FIELD_MAPPING', payload: p });
export const removeFieldMapping = (p: string): MappingEditorAction => ({ type: 'REMOVE_FIELD_MAPPING', payload: p });
export const updateFieldMapping = (p: Partial<FieldMapping> & { id: string }): MappingEditorAction => ({ type: 'UPDATE_FIELD_MAPPING', payload: p });
export const setFieldMappings = (p: FieldMapping[]): MappingEditorAction => ({ type: 'SET_FIELD_MAPPINGS', payload: p });
export const setArrayMappings = (p: ArrayMapping[]): MappingEditorAction => ({ type: 'SET_ARRAY_MAPPINGS', payload: p });
export const setSelectedPartner = (partnerId: number | null, adapterProperties: Record<string, string>): MappingEditorAction => ({ type: 'SET_SELECTED_PARTNER', payload: { partnerId, adapterProperties } });
export const addArrayMapping = (p: Omit<ArrayMapping, 'id'>): MappingEditorAction => ({ type: 'ADD_ARRAY_MAPPING', payload: p });
export const removeArrayMapping = (p: string): MappingEditorAction => ({ type: 'REMOVE_ARRAY_MAPPING', payload: p });
export const updateArrayMapping = (p: Partial<ArrayMapping> & { id: string }): MappingEditorAction => ({ type: 'UPDATE_ARRAY_MAPPING', payload: p });
export const selectMapping = (p: string | null): MappingEditorAction => ({ type: 'SELECT_MAPPING', payload: p });
export const setHoveredPath = (p: string | null): MappingEditorAction => ({ type: 'SET_HOVERED_PATH', payload: p });
export const setSearchInput = (p: string): MappingEditorAction => ({ type: 'SET_SEARCH_INPUT', payload: p });
export const setSearchOutput = (p: string): MappingEditorAction => ({ type: 'SET_SEARCH_OUTPUT', payload: p });
export const toggleNodeCollapsed = (p: string): MappingEditorAction => ({ type: 'TOGGLE_NODE_COLLAPSED', payload: p });
export const openArrayModal = (p: { id: string | null; presetTarget?: string; parentArrayId?: string | null; isRootOutput?: boolean }): MappingEditorAction => ({ type: 'OPEN_ARRAY_MODAL', payload: p });
export const togglePreview = (): MappingEditorAction => ({ type: 'TOGGLE_PREVIEW' });
export const setValidationErrors = (p: ValidationError[]): MappingEditorAction => ({ type: 'SET_VALIDATION_ERRORS', payload: p });
export const toggleValidation = (): MappingEditorAction => ({ type: 'TOGGLE_VALIDATION' });
export const undo = (): MappingEditorAction => ({ type: 'UNDO' });
export const redo = (): MappingEditorAction => ({ type: 'REDO' });
export const clearAll = (): MappingEditorAction => ({ type: 'CLEAR_ALL' });
export const autoMatch = (): MappingEditorAction => ({ type: 'AUTO_MATCH' });
export const generateFromTargetJson = (): MappingEditorAction => ({ type: 'GENERATE_FROM_TARGET_JSON' });
