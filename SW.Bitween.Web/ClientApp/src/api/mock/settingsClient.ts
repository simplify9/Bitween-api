import type { ApiClient } from "../client";
import { ApiRequestError, type SettingRow } from "../types";
import { delay, loadDb, saveDb } from "./store";

const fail = (code: string, message: string): never => {
  throw new ApiRequestError(code, message);
};

const settingRow = (setting: { value: string | null } & Omit<SettingRow, "overridden">): SettingRow => ({
  ...setting,
  overridden: setting.value !== null,
});

export const settingsClient: Pick<ApiClient, "listSettings" | "updateSetting"> = {
  async listSettings() {
    await delay();
    const db = loadDb();
    return db.settings.map(settingRow);
  },

  async updateSetting(key, value) {
    await delay();
    const db = loadDb();
    const setting = db.settings.find((s) => s.key === key);
    if (!setting) fail("NOT_FOUND", "This setting no longer exists.");
    setting!.value = value === null ? null : value.trim();
    saveDb(db);
    return settingRow(setting!);
  },
};
