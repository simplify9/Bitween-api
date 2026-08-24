import React, { useEffect, useState } from "react";
import {
  useMappingEditorDispatch,
  useMappingEditorState,
  addArrayMapping,
  openArrayModal,
  removeArrayMapping,
  updateArrayMapping,
} from "../../../lib/mapping/MappingEditorContext";
import { type FilterOperator, type LookupDictionary } from "../../../lib/mapping/types";
import { buildTypeMap } from "../../../lib/mapping/scribanGenerator";
import {
  getFullSourcePath,
  getFullTargetBase,
  getItemArrayPaths,
  collectArrayPaths,
  getItemProperties,
} from "../../../lib/mapping/arrayMappingHelpers";
import type { GlobalValuesSetRow } from "../../../api";
import { useGlobalSets } from "../data";

// ─── Pending mapping row type ─────────────────────────────────────────────────

export interface PendingMapping {
  id: string;
  source: string;
  target: string;
  transform?: string;
  fixedValue?: string;
  lookupDictionary?: LookupDictionary;
  isRootSource?: boolean;
  partnerPropKey?: string;
  globalSetId?: string;
  globalKey?: string;
}

// ─── Return type ──────────────────────────────────────────────────────────────

export interface UseArrayMappingModalResult {
  // Derived state
  isCreating: boolean;
  isNested: boolean;
  isRootOutput: boolean;
  parentAmTarget: string;
  // Form fields
  source: string;
  target: string;
  alias: string;
  hasFilter: boolean;
  filterField: string;
  filterOp: FilterOperator;
  filterValue: string;
  pendingMappings: PendingMapping[];
  openPanels: Record<string, "transform" | "lookup" | "fixed" | null>;
  // Derived data
  sourceArrayPaths: string[];
  sourceItemProps: string[];
  targetItemProps: string[];
  inputScalarProps: string[];
  outputTypeMap: Record<string, "string" | "number" | "boolean">;
  usedTargets: Set<string>;
  childArrayMappings: ReturnType<typeof useMappingEditorState>["arrayMappings"];
  allGlobalSets: GlobalValuesSetRow[];
  partnerAdapterProperties: Record<string, string>;
  // Setters
  setSource: (v: string) => void;
  setAlias: (v: string) => void;
  setHasFilter: (v: boolean) => void;
  setFilterField: (v: string) => void;
  setFilterOp: (v: FilterOperator) => void;
  setFilterValue: (v: string) => void;
  setPendingMappings: React.Dispatch<React.SetStateAction<PendingMapping[]>>;
  setOpenPanels: React.Dispatch<React.SetStateAction<Record<string, "transform" | "lookup" | "fixed" | null>>>;
  // Actions
  handleSave: () => void;
  handleClose: () => void;
  handleDelete: () => void;
  addFieldRow: () => void;
  removeFieldRow: (id: string) => void;
  patchFieldRow: (id: string, patch: Partial<PendingMapping>) => void;
  getPanel: (m: PendingMapping) => "transform" | "lookup" | "fixed" | null;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useArrayMappingModal(): UseArrayMappingModalResult {
  const dispatch = useMappingEditorDispatch();
  const {
    editingArrayId,
    arrayMappings,
    fieldMappings,
    inputJson,
    outputJson,
    newArrayPresetTarget,
    newArrayParentId,
    newArrayIsRootOutput,
    partnerAdapterProperties,
  } = useMappingEditorState();
  const allGlobalSets = useGlobalSets();

  const am = arrayMappings.find((m) => m.id === editingArrayId);
  const parentAm = newArrayParentId ? arrayMappings.find((m) => m.id === newArrayParentId) : undefined;

  const [source, setSource] = useState("");
  // (legacy vestigial `const [target] = useState(...)` dropped — never read; the hook
  //  exposes `currentTarget` instead. Removing it is a no-op under the host's noUnusedLocals.)
  const [alias, setAlias] = useState("item");
  const [hasFilter, setHasFilter] = useState(false);
  const [filterField, setFilterField] = useState("");
  const [filterOp, setFilterOp] = useState<FilterOperator>("!=");
  const [filterValue, setFilterValue] = useState("");
  const [pendingMappings, setPendingMappings] = useState<PendingMapping[]>([]);
  const [openPanels, setOpenPanels] = useState<Record<string, "transform" | "lookup" | "fixed" | null>>({});

  // ── Derived memos ──────────────────────────────────────────────────────────

  const parentFullSource = React.useMemo(() => {
    if (!parentAm) return "";
    return getFullSourcePath(parentAm.id, arrayMappings);
  }, [parentAm, arrayMappings]);

  const sourceArrayPaths = React.useMemo(() => {
    try {
      const parsed = JSON.parse(inputJson);
      if (Array.isArray(parsed)) {
        if (!parentAm) return ["items"];
        // Nested inside a root-array AM: find array-valued keys inside the first item
        if (parsed.length > 0 && typeof parsed[0] === "object" && parsed[0] !== null) {
          return Object.entries(parsed[0] as Record<string, unknown>)
            .filter(([, v]) => Array.isArray(v))
            .map(([k]) => k);
        }
        return [];
      }
      const obj = parsed as Record<string, unknown>;
      return parentAm ? getItemArrayPaths(obj, parentFullSource) : collectArrayPaths(obj);
    } catch { return []; }
  }, [inputJson, parentAm, parentFullSource]);

  const inputScalarProps = React.useMemo(() => {
    try {
      const parsed = JSON.parse(inputJson);
      // Root array — expose scalar props from the first element, prefixed with 'item.'
      const root: Record<string, unknown> = Array.isArray(parsed)
        ? (parsed.length > 0 && typeof parsed[0] === "object" && parsed[0] !== null ? parsed[0] as Record<string, unknown> : {})
        : parsed as Record<string, unknown>;
      const collect = (obj: Record<string, unknown>, prefix: string): string[] => {
        if (obj === null || typeof obj !== "object" || Array.isArray(obj)) return prefix ? [prefix] : [];
        return Object.entries(obj).flatMap(([k, v]) => {
          const path = prefix ? `${prefix}.${k}` : k;
          if (Array.isArray(v)) return [];
          if (v && typeof v === "object") return collect(v as Record<string, unknown>, path);
          return [path];
        });
      };
      return collect(root, "");
    } catch { return []; }
  }, [inputJson]);

  const fullSourcePath = React.useMemo(() => {
    if (!source) return "";
    // 'items' is a virtual C# alias for root-array input — not a real JSON key.
    // When nested inside a root-array AM, use source directly so path navigation works.
    if (parentFullSource === "items") {
      try { if (Array.isArray(JSON.parse(inputJson))) return source; } catch { /* ignore */ }
    }
    return parentFullSource ? `${parentFullSource}.${source}` : source;
  }, [source, parentFullSource, inputJson]);

  const sourceItemProps = React.useMemo(
    () => getItemProperties(inputJson, fullSourcePath),
    [inputJson, fullSourcePath],
  );

  const fullTargetBase = React.useMemo(() => {
    const t = am?.target ?? newArrayPresetTarget ?? "";
    // isRootOutput AMs have an empty target — keep as '' (sentinel for root array)
    if (!t) return newArrayIsRootOutput || am?.isRootOutput ? "__root__" : "";
    if (!newArrayParentId) return t;
    const parentBase = getFullTargetBase(newArrayParentId, arrayMappings);
    return parentBase ? `${parentBase}.${t}` : t;
  }, [am, newArrayPresetTarget, newArrayParentId, newArrayIsRootOutput, arrayMappings]);

  const targetItemProps = React.useMemo(() => {
    if (!fullTargetBase) return [];
    const results: string[] = [];
    if (fullTargetBase === "__root__") {
      // Root array output: get scalar/object item properties from the first element,
      // excluding array-valued keys (those are handled as nested array mappings).
      try {
        const parsed = JSON.parse(outputJson);
        if (Array.isArray(parsed) && parsed.length > 0 && typeof parsed[0] === "object" && parsed[0] !== null) {
          const first = parsed[0] as Record<string, unknown>;
          Object.entries(first)
            .filter(([, v]) => !Array.isArray(v))
            .forEach(([k]) => results.push(k));
        }
      } catch { /* ignore */ }
    } else {
      results.push(...getItemProperties(outputJson, fullTargetBase));
    }
    const arrayPrefix = fullTargetBase === "__root__" ? "[*]." : `${fullTargetBase}[*].`;
    fieldMappings
      .map((fm) => fm.target)
      .filter((t) => t.startsWith(arrayPrefix))
      .map((t) => t.slice(arrayPrefix.length))
      .filter(Boolean)
      .forEach((t) => results.push(t));
    arrayMappings
      .filter((a) => a.target.replace(/\[\*\]$/, "") === (am?.target ?? newArrayPresetTarget ?? "") && a.parentArrayId === (newArrayParentId ?? undefined))
      .flatMap((a) => a.mappings.map((m) => m.target))
      .filter(Boolean)
      .forEach((t) => results.push(t));
    return [...new Set(results)];
  }, [outputJson, fullTargetBase, am, newArrayPresetTarget, newArrayParentId, fieldMappings, arrayMappings]);

  const outputTypeMap = React.useMemo(() => {
    try { return buildTypeMap(outputJson); } catch { return {}; }
  }, [outputJson]);

  const usedTargets = React.useMemo(
    () => new Set(pendingMappings.map((m) => m.target).filter(Boolean)),
    [pendingMappings],
  );

  const childArrayMappings = React.useMemo(
    () => (am ? arrayMappings.filter((c) => c.parentArrayId === am.id) : []),
    [am, arrayMappings],
  );

  // ── Sync effect ────────────────────────────────────────────────────────────

  useEffect(() => {
    if (am) {
      setSource(am.source);
      setAlias(am.alias);
      setHasFilter(Boolean(am.filter));
      setFilterField(am.filter?.field ?? "");
      setFilterOp(am.filter?.operator ?? "!=");
      setFilterValue(String(am.filter?.value ?? ""));
      const scalarSet = new Set(inputScalarProps);
      const fullSrcPath = parentFullSource ? `${parentFullSource}.${am.source}` : am.source;
      const itemPropsSet = new Set(getItemProperties(inputJson, fullSrcPath));
      setPendingMappings(am.mappings.map((m) => ({
        ...m,
        isRootSource: m.isRootSource ?? ((Boolean(m.source) && scalarSet.has(m.source) && !itemPropsSet.has(m.source)) || undefined),
      })));
    } else {
      setSource("");
      setAlias("item");
      setHasFilter(false);
      setFilterField("");
      setFilterOp("!=");
      setFilterValue("");
      setPendingMappings([]);
    }
    setOpenPanels({});
  }, [am, inputScalarProps, editingArrayId]);

  // ── Actions ────────────────────────────────────────────────────────────────

  const isCreating = !am || editingArrayId === "__new__";
  const isNested = Boolean(newArrayParentId);
  const currentTarget = am?.target ?? newArrayPresetTarget ?? "";

  const getPanel = (m: PendingMapping): "transform" | "lookup" | "fixed" | null => {
    if (m.id in openPanels) return openPanels[m.id];
    if (m.fixedValue !== undefined) return "fixed";
    if ((m.lookupDictionary?.entries?.length ?? 0) > 0) return "lookup";
    if (m.transform) return "transform";
    return null;
  };

  const patchFieldRow = (id: string, patch: Partial<PendingMapping>) => {
    setPendingMappings((prev) => prev.map((p) => p.id === id ? { ...p, ...patch } : p));
  };

  const addFieldRow = () => {
    setPendingMappings((prev) => [...prev, { id: `pending-${Date.now()}`, source: "", target: "" }]);
  };

  const removeFieldRow = (id: string) => {
    setPendingMappings((prev) => prev.filter((p) => p.id !== id));
    setOpenPanels((prev) => { const next = { ...prev }; delete next[id]; return next; });
  };

  const handleSave = () => {
    const filter = hasFilter && filterField
      ? { field: filterField, operator: filterOp, value: filterValue }
      : undefined;
    const cleanMappings = (maps: PendingMapping[]) => maps.map((m) => {
      const hasValidEntries = (m.lookupDictionary?.entries ?? []).some((e) => e.from.trim() !== "" && e.to.trim() !== "");
      return hasValidEntries ? { ...m } : { ...m, lookupDictionary: undefined };
    });
    if (isCreating) {
      dispatch(addArrayMapping({
        source,
        target: currentTarget,
        alias: alias || "item",
        filter,
        mappings: cleanMappings(pendingMappings),
        parentArrayId: newArrayParentId ?? undefined,
        isRootOutput: newArrayIsRootOutput || undefined,
      }));
    } else {
      dispatch(updateArrayMapping({
        id: am!.id,
        source,
        target: currentTarget,
        alias: alias || "item",
        filter,
        mappings: cleanMappings(pendingMappings),
        isRootOutput: am?.isRootOutput || newArrayIsRootOutput || undefined,
      }));
    }
    dispatch(openArrayModal({ id: null }));
  };

  const handleClose = () => dispatch(openArrayModal({ id: null }));

  const handleDelete = () => {
    if (am) dispatch(removeArrayMapping(am.id));
    handleClose();
  };

  return {
    isCreating,
    isNested,
    isRootOutput: am?.isRootOutput ?? newArrayIsRootOutput,
    parentAmTarget: parentAm?.target ?? "",
    source,
    target: currentTarget,
    alias,
    hasFilter,
    filterField,
    filterOp,
    filterValue,
    pendingMappings,
    openPanels,
    sourceArrayPaths,
    sourceItemProps,
    targetItemProps,
    inputScalarProps,
    outputTypeMap,
    usedTargets,
    childArrayMappings,
    allGlobalSets,
    partnerAdapterProperties,
    setSource,
    setAlias,
    setHasFilter,
    setFilterField,
    setFilterOp,
    setFilterValue,
    setPendingMappings,
    setOpenPanels,
    handleSave,
    handleClose,
    handleDelete,
    addFieldRow,
    removeFieldRow,
    patchFieldRow,
    getPanel,
  };
}
