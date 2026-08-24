import type { ApiClient } from "../client";
import type { AdapterInfo, AdapterKind, AdapterProp } from "../types";
import { get } from "./request";

interface RawVersionedAdapter {
  key: string;
  versions: string[] | null;
}
interface RawStartupValue {
  optional: boolean;
  default: string | null;
  private: boolean;
  description: string | null;
}

// The backend's Prefix param takes the plural, lowercase form.
const KIND_PREFIX: Record<AdapterKind, string> = {
  receiver: "receivers",
  handler: "handlers",
  mapper: "mappers",
  validator: "validators",
};

async function fetchProps(id: string): Promise<AdapterProp[]> {
  const values = await get<Record<string, RawStartupValue>>(`/adapters/${encodeURIComponent(id)}/GetStartupValues`);
  return Object.entries(values ?? {}).map(([key, v]) => ({
    key,
    optional: v.optional,
    default: v.default ?? undefined,
    secret: v.private,
    description: v.description ?? undefined,
  }));
}

export const adapterMethods = {
  async listAdapters(kind: AdapterKind): Promise<AdapterInfo[]> {
    const rows = await get<RawVersionedAdapter[]>(`/adapters/Versioned?prefix=${KIND_PREFIX[kind]}`);
    return Promise.all(
      (rows ?? []).map(async (r) => ({
        id: r.key,
        kind,
        // No backend source for a friendly display name — fall back to the raw id.
        label: r.key,
        native: r.key.toLowerCase().startsWith("native"),
        versions: r.versions ?? [],
        // Legacy (non-native) adapters can fail to report startup values (e.g. their
        // serverless runtime isn't available locally) — don't let that blank out the
        // whole catalog, including the native adapters that did resolve fine.
        props: await fetchProps(r.key).catch(() => []),
      })),
    );
  },
} satisfies Partial<ApiClient>;
