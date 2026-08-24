import React, { useCallback, useState } from "react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import { getValueAtPath } from "../../../lib/mapping/mappingUtils";
import type { FieldMapping, LookupDictionary, MappingMode } from "../../../lib/mapping/types";
import {
  useMappingEditorDispatch,
  useMappingEditorState,
  addFieldMapping,
  removeFieldMapping,
  selectMapping,
  updateFieldMapping,
} from "../../../lib/mapping/MappingEditorContext";
import { isFieldMappingPopulated } from "../../../lib/mapping/mappingTreeUtils";
import { MODE_INITIAL_FIELDS } from "../../../lib/mapping/mappingModeDefaults";
import type { GlobalValuesSetRow } from "../../../api";
import { useGlobalSets } from "../data";

// ─── Return type ──────────────────────────────────────────────────────────────

export interface UseNormalLeafResult {
  // Derived state
  mapping: FieldMapping | undefined;
  isMapped: boolean;
  isSelected: boolean;
  currentMode: MappingMode;
  hasLookup: boolean;
  lookupOpen: boolean;
  allGlobalSets: GlobalValuesSetRow[];
  partnerAdapterProperties: Record<string, string>;
  ref: React.RefCallback<HTMLElement>;
  // Row handlers
  handleDrop: (e: React.DragEvent) => void;
  handleSelect: () => void;
  switchMode: (next: MappingMode) => void;
  handleLookupToggle: (e: React.MouseEvent) => void;
  // LeafValueInput handlers
  onSourceChange: (v: string) => void;
  onFixedChange: (v: string) => void;
  onPartnerChange: (v: string) => void;
  onGlobalSetChange: (setId: string) => void;
  onGlobalKeyChange: (key: string) => void;
  // Row actions
  removeMapping: () => void;
  updateTransform: (val: string) => void;
  // Lookup actions
  patchLookup: (next: LookupDictionary | undefined) => void;
  removeLookup: () => void;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useNormalLeaf(
  node: TreeNode,
  onLeafRef: ((path: string, el: HTMLElement | null) => void) | undefined,
): UseNormalLeafResult {
  const dispatch = useMappingEditorDispatch();
  const {
    fieldMappings,
    selectedMappingId: selectedId,
    outputJson,
    partnerAdapterProperties,
  } = useMappingEditorState();
  const [lookupOpen, setLookupOpen] = useState(false);
  const allGlobalSets = useGlobalSets();

  const mapping = fieldMappings.find((m) => m.target === node.path);
  const isMapped = isFieldMappingPopulated(mapping);
  const isSelected = mapping ? selectedId === mapping.id : false;

  const currentMode: MappingMode =
    mapping?.partnerPropKey !== undefined ? "partner" :
    mapping?.globalSetId !== undefined ? "global" :
    mapping?.fixedValue !== undefined ? "fixed" :
    "source";

  const hasLookup = (mapping?.lookupDictionary?.entries?.length ?? 0) > 0;

  const ref = useCallback(
    (el: HTMLElement | null) => onLeafRef?.(node.path, el),
    [node.path, onLeafRef],
  );

  // ── Lookup helpers ─────────────────────────────────────────────────────────

  const patchLookup = (next: LookupDictionary | undefined) => {
    if (!mapping) return;
    dispatch(updateFieldMapping({ id: mapping.id, lookupDictionary: next }));
  };

  const removeLookup = () => {
    if (!mapping) return;
    dispatch(updateFieldMapping({ id: mapping.id, lookupDictionary: undefined }));
    setLookupOpen(false);
  };

  // ── Event handlers ─────────────────────────────────────────────────────────

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    const sourcePath = e.dataTransfer.getData("text/plain");
    if (!sourcePath) return;
    if (mapping) {
      dispatch(updateFieldMapping({ id: mapping.id, source: sourcePath, fixedValue: undefined, valuesSetId: undefined }));
    } else {
      dispatch(addFieldMapping({ source: sourcePath, target: node.path }));
    }
  };

  const handleSelect = () => {
    if (mapping) {
      dispatch(selectMapping(isSelected ? null : mapping.id));
    } else {
      dispatch(addFieldMapping({ source: "", target: node.path }));
    }
  };

  const switchMode = (next: MappingMode) => {
    const fields: Partial<FieldMapping> = { ...MODE_INITIAL_FIELDS[next] };

    if (next === "fixed") {
      try {
        const parsedOutput = JSON.parse(outputJson);
        if (!Array.isArray(parsedOutput)) {
          fields.fixedValue = getValueAtPath(parsedOutput, node.path);
        }
      } catch { /* ignore parse errors */ }
    }

    if (next !== "source") setLookupOpen(false);

    if (!mapping) {
      dispatch(addFieldMapping({ source: "", target: node.path, ...fields }));
    } else {
      dispatch(updateFieldMapping({ id: mapping.id, ...fields }));
    }
  };

  const handleLookupToggle = (e: React.MouseEvent) => {
    e.stopPropagation();
    setLookupOpen((v) => {
      if (v && mapping) {
        const entries = mapping.lookupDictionary?.entries ?? [];
        const hasValid = entries.some((entry) => entry.from.trim() !== "" && entry.to.trim() !== "");
        if (!hasValid) {
          dispatch(updateFieldMapping({ id: mapping.id, lookupDictionary: undefined }));
        }
      }
      return !v;
    });
  };

  // ── LeafValueInput handlers ────────────────────────────────────────────────

  const onSourceChange = (v: string) => {
    if (mapping) {
      dispatch(updateFieldMapping({ id: mapping.id, source: v, fixedValue: undefined, partnerPropKey: undefined, valuesSetId: undefined, lookupDictionary: undefined }));
    } else {
      dispatch(addFieldMapping({ source: v, target: node.path }));
    }
  };

  const onFixedChange = (v: string) => {
    if (!mapping) return;
    dispatch(updateFieldMapping({ id: mapping.id, fixedValue: v }));
  };

  const onPartnerChange = (v: string) => {
    if (!mapping) return;
    dispatch(updateFieldMapping({ id: mapping.id, partnerPropKey: v }));
  };

  const onGlobalSetChange = (setId: string) => {
    if (!mapping) return;
    dispatch(updateFieldMapping({ id: mapping.id, globalSetId: setId, globalKey: "" }));
  };

  const onGlobalKeyChange = (key: string) => {
    if (!mapping) return;
    dispatch(updateFieldMapping({ id: mapping.id, globalKey: key }));
  };

  const removeMapping = () => {
    if (!mapping) return;
    dispatch(removeFieldMapping(mapping.id));
  };

  const updateTransform = (val: string) => {
    if (!mapping) return;
    dispatch(updateFieldMapping({ id: mapping.id, transform: val || undefined }));
  };

  return {
    mapping,
    isMapped,
    isSelected,
    currentMode,
    hasLookup,
    lookupOpen,
    allGlobalSets,
    partnerAdapterProperties,
    ref,
    handleDrop,
    handleSelect,
    switchMode,
    handleLookupToggle,
    onSourceChange,
    onFixedChange,
    onPartnerChange,
    onGlobalSetChange,
    onGlobalKeyChange,
    removeMapping,
    updateTransform,
    patchLookup,
    removeLookup,
  };
}
