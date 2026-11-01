import { useSyncExternalStore } from "react";
import { backgroundState, isRunning, subscribeBackground, type BackgroundState } from "../api/http/background";

/** The work carried on in the background, kept current. */
export const useBackground = (): BackgroundState => useSyncExternalStore(subscribeBackground, backgroundState);

/**
 * How far the newest work of one kind under way has got - `POST /xchanges/bulkretry`, say - for a
 * page that shows it beside the header; null when none is running or it hasn't said.
 */
export function useBackgroundProgress(path: string): { done: number; total: number } | null {
  const { requests } = useBackground();
  const running = requests.find((r) => r.path === path && isRunning(r.status) && r.total != null);
  return running ? { done: running.done ?? 0, total: running.total! } : null;
}
