import type { ApiClient } from "../client";
import type { SettingRow } from "../types";
import { get, post, request } from "./request";

export const settingsMethods = {
  /**
   * The backend owns the catalog (label, section, kind, default, secret), so rows arrive
   * ready to render — including which section each belongs to and, for secrets, only
   * whether a value is set.
   */
  listSettings(): Promise<SettingRow[]> {
    return get<SettingRow[]>("/settings");
  },

  /** A null value resets the setting, which is a DELETE rather than a write. */
  async updateSetting(key: string, value: string | null): Promise<void> {
    const path = `/settings/${encodeURIComponent(key)}`;
    if (value === null) await request<void>(path, { method: "DELETE" });
    else await post(path, { value });
  },
} satisfies Partial<ApiClient>;
