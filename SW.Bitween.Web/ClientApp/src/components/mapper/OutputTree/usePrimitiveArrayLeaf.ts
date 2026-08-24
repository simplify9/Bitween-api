import React, { useCallback, useState } from "react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import type { PrimitiveArrayItem } from "../../../lib/mapping/types";
import {
  useMappingEditorDispatch,
  addArrayMapping,
  removeArrayMapping,
  updateArrayMapping,
} from "../../../lib/mapping/MappingEditorContext";

// ─── Return type ──────────────────────────────────────────────────────────────

export interface UsePrimitiveArrayLeafResult {
  panelOpen: boolean;
  setPanelOpen: React.Dispatch<React.SetStateAction<boolean>>;
  ref: React.RefCallback<HTMLElement>;
  mappedCount: number;
  saveItem: (idx: number, newItem: PrimitiveArrayItem) => void;
  onMapEmptyArray: () => void;
  onClearEmptyArray: () => void;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function usePrimitiveArrayLeaf(
  node: TreeNode,
  primAmId: string | undefined,
  currentItems: PrimitiveArrayItem[],
  onLeafRef: ((path: string, el: HTMLElement | null) => void) | undefined,
): UsePrimitiveArrayLeafResult {
  const dispatch = useMappingEditorDispatch();
  const [panelOpen, setPanelOpen] = useState(false);

  const ref = useCallback(
    (el: HTMLElement | null) => onLeafRef?.(node.path, el),
    [node.path, onLeafRef],
  );

  const mappedCount = currentItems.filter(
    (item) => item.source || item.fixedValue !== undefined || item.partnerPropKey || (item.globalSetId && item.globalKey),
  ).length;

  const isRootPrimArray = node.path === "[*]";

  const saveItem = (idx: number, newItem: PrimitiveArrayItem) => {
    const updated = currentItems.map((item, i) => (i === idx ? newItem : item));
    if (primAmId) {
      dispatch(updateArrayMapping({ id: primAmId, primitiveItems: updated }));
    } else {
      dispatch(addArrayMapping({ source: "", target: isRootPrimArray ? "" : node.path, alias: "item", mappings: [], primitiveItems: updated, isRootOutput: isRootPrimArray || undefined }));
    }
  };

  const onMapEmptyArray = () => {
    dispatch(addArrayMapping({ source: "", target: isRootPrimArray ? "" : node.path, alias: "item", mappings: [], primitiveItems: [], isRootOutput: isRootPrimArray || undefined }));
  };

  const onClearEmptyArray = () => {
    if (primAmId) dispatch(removeArrayMapping(primAmId));
  };

  return {
    panelOpen,
    setPanelOpen,
    ref,
    mappedCount,
    saveItem,
    onMapEmptyArray,
    onClearEmptyArray,
  };
}
