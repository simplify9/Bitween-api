import type React from "react";
import { useMemo } from "react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import { useMappingEditorState } from "../../../lib/mapping/MappingEditorContext";
import { getFullTargetPrefix, isFieldMappingPopulated } from "../../../lib/mapping/mappingTreeUtils";
import { useGlobalSets } from "../data";
import type { PrimitiveArrayItem } from "../../../lib/mapping/types";
import { ArrayInnerField } from "./ArrayInnerField";
import { PrimitiveArrayLeaf } from "./PrimitiveArrayLeaf";
import { NormalLeaf } from "./NormalLeaf";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface OutputLeafProps {
  node: TreeNode;
  sourcePaths: string[];
  typeMap: Record<string, 'string' | 'number' | 'boolean'>;
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
  isSearchMatch?: boolean;
}

// ─── OutputLeaf ───────────────────────────────────────────────────────────────
// Thin router: determines which leaf variant to render based on the node type.

export const OutputLeaf: React.FC<OutputLeafProps> = ({ node, sourcePaths, typeMap, onLeafRef, isSearchMatch }) => {
  const { arrayMappings, outputJson, partnerAdapterProperties } = useMappingEditorState();
  const allGlobalSets = useGlobalSets();

  // ── Array inner field detection (derived — no hooks after this) ───────────
  const isArrayField = node.path.includes('[*].');

  // ── Primitive array detection — must run unconditionally (hooks rules) ────
  const primitiveArrayValues = useMemo((): unknown[] | null => {
    if (isArrayField) return null;
    try {
      const parsed = JSON.parse(outputJson);
      // Root primitive array: node.path is '[*]' sentinel (no named key)
      if (Array.isArray(parsed)) {
        if (node.path === '[*]' && parsed.every((v) => v === null || typeof v !== 'object')) {
          return parsed as unknown[];
        }
        return null;
      }
      const obj = parsed as Record<string, unknown>;
      const cur = node.path.split('.').reduce<unknown>((o, k) => {
        if (o == null || typeof o !== 'object') return undefined;
        return (o as Record<string, unknown>)[k];
      }, obj);
      if (Array.isArray(cur) && cur.every((v) => v === null || typeof v !== 'object')) {
        return cur as unknown[];
      }
      return null;
    } catch {
      return null;
    }
  }, [isArrayField, node.path, outputJson]);

  // ── Array inner field ─────────────────────────────────────────────────────
  if (isArrayField) {
    const lastStarIdx = node.path.lastIndexOf('[*].');
    const arrayParentTarget = node.path.substring(0, lastStarIdx);
    const relativeKey = node.path.substring(lastStarIdx + 4);
    const arrayOwner = arrayMappings.find(
      (am) => getFullTargetPrefix(am.id, arrayMappings) === arrayParentTarget
    );
    const innerMapping = arrayOwner?.mappings.find((m) => m.target === relativeKey);

    return (
      <ArrayInnerField
        node={node}
        innerMapping={innerMapping}
        isMapped={isFieldMappingPopulated(innerMapping)}
        onLeafRef={onLeafRef}
        isSearchMatch={isSearchMatch}
        onOpenArrayModal={() => {/* managed by parent OutputBranch */}}
      />
    );
  }

  // ── Primitive array ───────────────────────────────────────────────────────
  if (primitiveArrayValues !== null) {
    const primAm = node.path === '[*]'
      ? arrayMappings.find((am) => am.isRootOutput && !am.parentArrayId && am.primitiveItems)
      : arrayMappings.find((am) => am.target === node.path && !am.parentArrayId && am.primitiveItems);
    const currentItems: PrimitiveArrayItem[] = primAm?.primitiveItems ?? primitiveArrayValues.map(() => ({}));
    return (
      <PrimitiveArrayLeaf
        node={node}
        sourcePaths={sourcePaths}
        primitiveArrayValues={primitiveArrayValues}
        primAmId={primAm?.id}
        currentItems={currentItems}
        partnerAdapterProperties={partnerAdapterProperties}
        allGlobalSets={allGlobalSets}
        onLeafRef={onLeafRef}
        isSearchMatch={isSearchMatch}
      />
    );
  }

  // ── Normal field leaf ─────────────────────────────────────────────────────
  return (
    <NormalLeaf
      node={node}
      sourcePaths={sourcePaths}
      targetFieldType={typeMap[node.path]}
      onLeafRef={onLeafRef}
      isSearchMatch={isSearchMatch}
    />
  );
};
