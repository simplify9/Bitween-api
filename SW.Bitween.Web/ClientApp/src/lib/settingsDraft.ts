import { useSyncExternalStore } from "react";
import type { SettingRow } from "../api";

/**
 * Pending (unsaved) setting changes: key → new value, or null for a pending
 * "reset to default". Lives in sessionStorage so the draft survives leaving
 * the Settings page — the whole app renders with the draft applied, letting
 * you walk around and preview a change (the brand color, the footer links…)
 * before committing it. Saving writes every entry through the API; discarding
 * clears the draft and the preview reverts instantly.
 */
export type SettingsDraft = Record<string, string | null>;

const KEY = "bitween-settings-draft";

const load = (): SettingsDraft => {
  try {
    return JSON.parse(sessionStorage.getItem(KEY) ?? "{}") as SettingsDraft;
  } catch {
    return {};
  }
};

let cache: SettingsDraft = load();
const listeners = new Set<() => void>();

const persist = () => {
  sessionStorage.setItem(KEY, JSON.stringify(cache));
  listeners.forEach((l) => l());
};

export const settingsDraft = {
  get: (): SettingsDraft => cache,

  /** Stage a change; passing the row's current saved value removes the stage. */
  stage(row: SettingRow, value: string | null) {
    const unchanged = value === row.value || (value !== null && !row.overridden && value === row.defaultValue);
    if (unchanged) {
      if (!(row.key in cache)) return;
      const { [row.key]: _dropped, ...rest } = cache;
      cache = rest;
    } else {
      cache = { ...cache, [row.key]: value };
    }
    persist();
  },

  discardAll() {
    cache = {};
    persist();
  },
};

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

/** Reactive view of the draft — updates any component when it changes. */
export function useSettingsDraft(): SettingsDraft {
  return useSyncExternalStore(subscribe, () => cache);
}

/** What a setting currently *shows as*: draft > saved override > default. */
export function effectiveValue(row: SettingRow, draft: SettingsDraft): string {
  if (row.key in draft) return draft[row.key] ?? row.defaultValue;
  return row.value ?? row.defaultValue;
}
