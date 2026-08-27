import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import {
  useMappingEditorDispatch,
  loadEditorContext,
} from "../../lib/mapping/MappingEditorContext";
import { recordToKvps } from "./data";

// Handles the two data-loading effects: clear-on-id-change and populate-on-data-arrive.
// Fully self-contained — callers get no return value.
export function useMappingEditorLoader(subscriptionId: number): void {
  const dispatch = useMappingEditorDispatch();
  const { data: subscriptionData } = useQuery({
    queryKey: ["subscription", subscriptionId],
    queryFn: () => api.getSubscription(subscriptionId),
    enabled: !!subscriptionId,
  });
  const [, setLoadedForId] = useState<number | null>(null);
  const pendingIdRef = useRef<number | null>(null);

  // Clear editor state immediately when subscription changes
  useEffect(() => {
    pendingIdRef.current = subscriptionId || null;
    dispatch(loadEditorContext({ subscriptionId: subscriptionId || 0, mapperProperties: [] }));
    setLoadedForId(null);
  }, [subscriptionId]);

  // Populate state once the correct data arrives
  useEffect(() => {
    if (!subscriptionData || !pendingIdRef.current) return;
    if (pendingIdRef.current !== subscriptionId) return;
    dispatch(
      loadEditorContext({
        subscriptionId,
        mapperId: subscriptionData.mapperId,
        mapperProperties: recordToKvps(subscriptionData.mapperProperties),
      })
    );
    setLoadedForId(subscriptionId);
  }, [subscriptionData]);
}
