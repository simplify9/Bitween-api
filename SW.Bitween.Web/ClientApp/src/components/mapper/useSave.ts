import { useCallback, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import {
  useMappingEditorDispatch,
  useMappingEditorState,
  setArrayMappings,
  setFieldMappings,
  setMode,
  setValidationErrors,
  syncManualTemplate,
} from "../../lib/mapping/MappingEditorContext";
import { NATIVE_JSON_MAPPER_ID, type ValidationError, type KeyValuePair } from "../../lib/mapping/types";
import { generateScriban, parseScriban, resolveParentArrayIds } from "../../lib/mapping/scribanGenerator";
import { kvpsToRecord, useValuesSetMap } from "./data";

// ─── Return type ──────────────────────────────────────────────────────────────

export interface UseSaveResult {
  isSaving: boolean;
  saveSuccess: boolean;
  handleValidate: () => void;
  handleSave: () => Promise<void>;
  handleModeChange: (newMode: "visual" | "manual") => void;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useSave(subscriptionId: number): UseSaveResult {
  const dispatch = useMappingEditorDispatch();
  const queryClient = useQueryClient();
  const {
    mode,
    fieldMappings,
    arrayMappings,
    inputJson,
    outputJson,
    manualTemplate,
    isManualDirty,
    selectedPartnerId,
  } = useMappingEditorState();
  const saveMapper = useMutation({
    mutationFn: (props: KeyValuePair[]) =>
      api.updateSubscription(subscriptionId, {
        mapperId: NATIVE_JSON_MAPPER_ID,
        mapperProperties: kvpsToRecord(props),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["subscription", subscriptionId] }),
  });
  const isSaving = saveMapper.isPending;
  const [saveSuccess, setSaveSuccess] = useState(false);
  const valuesSetMap = useValuesSetMap();

  // ── Validate ───────────────────────────────────────────────────────────────

  const handleValidate = useCallback(() => {
    const errors: ValidationError[] = [];
    for (const m of fieldMappings) {
      if (!m.target.trim()) {
        errors.push({ type: "error", message: `Mapping has no target field`, path: m.id });
      }
      if (!m.source && m.fixedValue === undefined && !m.partnerPropKey && !(m.globalSetId && m.globalKey)) {
        errors.push({
          type: "warning",
          message: `"${m.target}" has no assigned source, fixed value, partner property, or global variable`,
          path: m.target,
        });
      }
    }
    for (const am of arrayMappings) {
      if (!am.source && !am.fixedItems?.length && am.primitiveItems == null)
        errors.push({ type: "error", message: `Array mapping missing source path` });
      if (!am.target && !am.isRootOutput)
        errors.push({ type: "error", message: `Array mapping missing target path` });
      if (am.mappings.length === 0 && !am.fixedItems?.length && am.primitiveItems == null) {
        const label = am.isRootOutput ? `root array (${am.source})` : `${am.source} → ${am.target}`;
        errors.push({
          type: "warning",
          message: `Array mapping "${label}" has no field mappings`,
        });
      }
    }
    dispatch(setValidationErrors(errors));
  }, [fieldMappings, arrayMappings, dispatch]);

  // ── Persist ────────────────────────────────────────────────────────────────

  const persistSave = useCallback(async () => {
    const templateToSave =
      mode === "manual" && manualTemplate
        ? manualTemplate
        : generateScriban(fieldMappings, arrayMappings, valuesSetMap, outputJson, inputJson);

    const mapperProperties: KeyValuePair[] = [
      { key: "ScribanTemplate", value: templateToSave },
    ];
    if (arrayMappings.length > 0) {
      mapperProperties.push({ key: "ArrayRules", value: JSON.stringify(arrayMappings) });
    }
    if (inputJson.trim()) {
      mapperProperties.push({ key: "SourceJson", value: inputJson });
    }
    if (outputJson.trim()) {
      mapperProperties.push({ key: "TargetJson", value: outputJson });
    }
    if (selectedPartnerId != null) {
      mapperProperties.push({ key: "PartnerId", value: String(selectedPartnerId) });
    }

    await saveMapper.mutateAsync(mapperProperties);
    setSaveSuccess(true);
    setTimeout(() => setSaveSuccess(false), 2000);
  }, [
    mode,
    manualTemplate,
    fieldMappings,
    arrayMappings,
    valuesSetMap,
    outputJson,
    inputJson,
    selectedPartnerId,
    saveMapper,
  ]);

  const handleSave = useCallback(async () => {
    handleValidate();
    await persistSave();
  }, [handleValidate, persistSave]);

  // ── Mode change (syncs Manual ↔ Visual template) ───────────────────────────

  const handleModeChange = useCallback(
    (newMode: "visual" | "manual") => {
      if (mode === "manual" && newMode !== "manual" && isManualDirty && manualTemplate) {
        const parsed = parseScriban(manualTemplate);
        dispatch(
          setFieldMappings(
            parsed.fieldMappings.map((m, i) => ({ id: `parsed-${i}-${Date.now()}`, ...m }))
          )
        );
        const now = Date.now();
        const rawAMs = parsed.arrayMappings.map((am, i) => ({
          id: `parsed-am-${i}-${now}`,
          ...am,
          mappings: am.mappings.map((m, j) => ({
            id: `parsed-am-${i}-${j}-${now}`,
            ...m,
          })),
        }));
        dispatch(setArrayMappings(resolveParentArrayIds(rawAMs)));
      } else if (newMode === "manual") {
        dispatch(syncManualTemplate(generateScriban(fieldMappings, arrayMappings, valuesSetMap, outputJson)));
      }
      dispatch(setMode(newMode));
    },
    [mode, isManualDirty, manualTemplate, fieldMappings, arrayMappings, valuesSetMap, dispatch]
  );

  return { isSaving, saveSuccess, handleValidate, handleSave, handleModeChange };
}
