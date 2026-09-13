import { useCallback, useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/RulesEditorContext";
import { loadMapping, saveMapping, toWire } from "../../lib/nativeMapper/serialize";
import { NATIVE_MAPPER_ID } from "../../lib/nativeMapper/types";

/**
 * Where the editor reads its mapping from and writes it back to.
 *
 * `subscription` is the editor's original home: a saved record, read by id and written
 * with its own request. `draft` is a subscription that does not exist yet — the create
 * pages hold it in memory, so there is no id to read and nothing to PATCH.
 *
 * Only these two hooks ever knew about the id. Everything else in the editor — the
 * rules, the samples, the preview — already worked on values alone, and the preview
 * endpoint is stateless (rules + sample + partner), so a mapping can be built and
 * checked against real output before anything is saved.
 */
export type MappingTarget =
  | { kind: "subscription"; subscriptionId: number }
  | {
      kind: "draft";
      /** What the draft's mapper slot is set to, for the "this would replace" question. */
      mapperId: string | null;
      mapperProperties: Record<string, string>;
      /** Whose values the preview substitutes; the create pages know it before saving. */
      partnerId: number | null;
      /** Hands the rules back to the page holding the draft. */
      onSave: (mapperProperties: Record<string, string>) => void;
    };

/** Loads the target's rules into the editor, and clears them when the target changes. */
export function useMappingLoader(target: MappingTarget) {
  const dispatch = useRulesDispatch();
  const subscriptionId = target.kind === "subscription" ? target.subscriptionId : 0;
  const { data } = useQuery({
    queryKey: keys.subscriptions.detail(subscriptionId),
    queryFn: () => api.getSubscription(subscriptionId),
    enabled: Boolean(subscriptionId),
  });

  // The subscription whose data we are waiting for. Without this, switching
  // subscriptions quickly can land the first one's rules in the second one's editor.
  const awaiting = useRef<number | null>(null);

  useEffect(() => {
    awaiting.current = subscriptionId || null;
  }, [subscriptionId]);

  useEffect(() => {
    if (!data || awaiting.current !== subscriptionId) return;
    const loaded = loadMapping(data.mapperProperties);
    dispatch({
      type: "LOAD",
      rules: loaded.rules,
      sourceSample: loaded.sourceSample,
      targetSample: loaded.targetSample,
      error: loaded.error,
    });
  }, [data, subscriptionId, dispatch]);

  // A draft has its rules already — nothing to wait for. Loaded once rather than on
  // every render: the page builds `mapperProperties` inline, so it is a new object each
  // time, and re-dispatching LOAD would throw away everything typed since.
  const draftProperties = target.kind === "draft" ? target.mapperProperties : null;
  const draftLoaded = useRef(false);
  useEffect(() => {
    if (draftProperties === null || draftLoaded.current) return;
    draftLoaded.current = true;
    const loaded = loadMapping(draftProperties);
    dispatch({
      type: "LOAD",
      rules: loaded.rules,
      sourceSample: loaded.sourceSample,
      targetSample: loaded.targetSample,
      error: loaded.error,
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draftProperties === null, dispatch]);

  if (target.kind === "draft") return { partnerId: target.partnerId };
  return { partnerId: data?.partnerId ?? null };
}

const PREVIEW_DEBOUNCE_MS = 500;

/**
 * Keeps the preview in step with the rules.
 *
 * Posts the rules and the sample and shows what comes back, which is the same
 * read/map/write the exchange pipeline runs. Nothing is generated here and nothing
 * is evaluated in the browser, so what the editor shows is what a partner receives.
 */
export function useMappingPreview(partnerId: number | null) {
  const { rules, sourceSample } = useRules();
  const dispatch = useRulesDispatch();
  const [isPreviewing, setPreviewing] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const runId = useRef(0);

  useEffect(() => {
    if (timer.current) clearTimeout(timer.current);

    if (!sourceSample.trim()) {
      // Bumped so a request already on its way is retired: without this, clearing the
      // sample cleared the preview and then the older response put a document back.
      runId.current++;
      dispatch({ type: "PREVIEW_RESULT", output: null, ruleErrors: {}, error: null });
      return;
    }

    timer.current = setTimeout(async () => {
      const run = ++runId.current;
      setPreviewing(true);
      try {
        const result = await api.previewMappingRules({
          mappingRules: JSON.stringify(toWire(rules)),
          sourceDocument: sourceSample,
          partnerId,
        });

        // A slower earlier request must not overwrite a newer result.
        if (run !== runId.current) return;

        dispatch({
          type: "PREVIEW_RESULT",
          output: result.outputDocument,
          ruleErrors: Object.fromEntries(result.ruleErrors.map((e) => [e.target, e.reason])),
          error: result.error,
        });
      } catch {
        if (run !== runId.current) return;
        dispatch({
          type: "PREVIEW_RESULT",
          output: null,
          ruleErrors: {},
          error: "Could not reach the server to work out the preview.",
        });
      } finally {
        if (run === runId.current) setPreviewing(false);
      }
    }, PREVIEW_DEBOUNCE_MS);

    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [rules, sourceSample, partnerId, dispatch]);

  return { isPreviewing };
}

/** Saves the rules onto the target, pointing it at this mapper. */
export function useMappingSave(target: MappingTarget) {
  const { rules, sourceSample, targetSample } = useRules();
  const dispatch = useRulesDispatch();
  const queryClient = useQueryClient();
  const [justSaved, setJustSaved] = useState(false);
  const subscriptionId = target.kind === "subscription" ? target.subscriptionId : 0;

  // Cached — the loader asked for this already.
  const { data } = useQuery({
    queryKey: keys.subscriptions.detail(subscriptionId),
    queryFn: () => api.getSubscription(subscriptionId),
    enabled: Boolean(subscriptionId),
  });

  // Saving switches the subscription to this mapper as well as storing the rules, so a
  // mapping built in the other editor is replaced rather than kept alongside. Worth
  // asking first: a template someone wrote by hand exists nowhere else once it is gone,
  // and reaching this editor no longer requires having saved the switch deliberately.
  //
  // A draft is asked the same question about the same thing — its own mapper slot, which
  // a create page can point at the old mapper before opening this one.
  const current =
    target.kind === "draft"
      ? { mapperId: target.mapperId, mapperProperties: target.mapperProperties }
      : { mapperId: data?.mapperId, mapperProperties: data?.mapperProperties };
  const replacing =
    current.mapperId &&
    current.mapperId !== NATIVE_MAPPER_ID &&
    Object.keys(current.mapperProperties ?? {}).length > 0
      ? current.mapperId
      : null;

  const mutation = useMutation({
    mutationFn: () =>
      api.updateSubscription(subscriptionId, {
        mapperId: NATIVE_MAPPER_ID,
        mapperProperties: saveMapping(rules, sourceSample, targetSample),
      }),
    onSuccess: () => {
      dispatch({ type: "SAVED" });
      setJustSaved(true);
      setTimeout(() => setJustSaved(false), 2000);
      return queryClient.invalidateQueries({
        queryKey: keys.subscriptions.detail(subscriptionId),
      });
    },
  });

  // A draft has nowhere to PATCH: the rules go back to the page holding it, and are
  // written when that page creates the subscription. Nothing can fail here, which is why
  // there is no error to show and no request to be pending.
  const onSaveDraft = target.kind === "draft" ? target.onSave : null;
  const saveDraft = useCallback(() => {
    onSaveDraft?.(saveMapping(rules, sourceSample, targetSample));
    dispatch({ type: "SAVED" });
    setJustSaved(true);
    setTimeout(() => setJustSaved(false), 2000);
    return Promise.resolve();
  }, [onSaveDraft, rules, sourceSample, targetSample, dispatch]);

  // Resolves either way. The failure is shown from `saveError`, so a rejection here
  // would only ever become an unhandled one — every caller fires this and moves on.
  const saveSubscription = useCallback(
    () => mutation.mutateAsync().then(
      () => undefined,
      () => undefined,
    ),
    [mutation],
  );

  return {
    save: onSaveDraft ? saveDraft : saveSubscription,
    isSaving: onSaveDraft ? false : mutation.isPending,
    justSaved,
    saveError: onSaveDraft || !mutation.error ? null : (mutation.error as Error).message,
    /** The other mapper whose stored mapping this save would replace, if any. */
    replacing,
  };
}

/**
 * Undo, redo and save from the keyboard.
 *
 * Kept from the old editor, which had them and which people are used to. The one
 * difference is deliberate: inside a text box, Ctrl+Z is left to the browser so it
 * undoes what was typed rather than the last change to the mapping. The old editor
 * took it in both cases, so a mistyped field name could only be fixed by undoing a
 * rule change somewhere else entirely.
 */
export function useMappingShortcuts(save: () => void) {
  const dispatch = useRulesDispatch();

  useEffect(() => {
    const handle = (e: KeyboardEvent) => {
      if (!e.ctrlKey && !e.metaKey) return;
      const key = e.key.toLowerCase();

      if (key === "s") {
        e.preventDefault();
        save();
        return;
      }

      const typing = (e.target as HTMLElement | null)?.closest("input, textarea");
      if (typing) return;

      if (key === "z" && !e.shiftKey) {
        e.preventDefault();
        dispatch({ type: "UNDO" });
      }
      if (key === "y" || (key === "z" && e.shiftKey)) {
        e.preventDefault();
        dispatch({ type: "REDO" });
      }
    };

    window.addEventListener("keydown", handle);
    return () => window.removeEventListener("keydown", handle);
  }, [dispatch, save]);
}
