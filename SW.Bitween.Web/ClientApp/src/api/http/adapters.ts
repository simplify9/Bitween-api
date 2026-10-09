import type { ApiClient } from "../client";
import type { AdapterInfo, AdapterKind, AdapterProp, AdapterSourceFile, AdapterSourceListing } from "../types";
import { get } from "./request";

interface RawStartupValue {
  optional: boolean;
  default: string | null;
  private: boolean;
  description: string | null;
}
interface RawCatalogAdapter {
  key: string;
  native: boolean;
  versions: string[] | null;
  startupValues: Record<string, RawStartupValue> | null;
  // From the catalog and manifest — absent from servers that predate them.
  currentVersion?: string | null;
  versionHistory?: {
    version: string;
    publishedOn?: string | null;
    publishedBy?: string | null;
    releaseNotes?: string | null;
    withdrawn?: boolean;
    hasSource?: boolean;
    runtime?: string | null;
  }[] | null;
  displayName?: string | null;
  summary?: string | null;
  description?: string | null;
  publisher?: string | null;
  icon?: string | null;
  tags?: string[] | null;
}

// The backend's Prefix param takes the plural, lowercase form.
const KIND_PREFIX: Record<AdapterKind, string> = {
  receiver: "receivers",
  handler: "handlers",
  mapper: "mappers",
  validator: "validators",
};

/**
 * What to call an adapter on screen.
 *
 * An adapter's id is its C# class name, so every in-process one is spelled
 * `NativeSomething` — and with no friendly name coming from the backend, that
 * prefix was what people read in every picker. It says nothing a user acts on:
 * where an adapter runs is the *other* list's business now, not part of its name.
 * Stripped for display only — `id` is untouched, and stays what a subscription
 * stores, what the row is searchable by, and what is shown when the catalog
 * doesn't know an adapter.
 */
function displayName(key: string): string {
  // "NativeMapper" would strip to a bare "Mapper", which names the kind rather
  // than the adapter and reads as the generic one next to "JSONMapper".
  if (key === "NativeMapper") return "Visual mapper";
  const stripped = key.replace(/^Native/, "");
  return stripped === "" ? key : stripped;
}

function toProps(values: Record<string, RawStartupValue> | null): AdapterProp[] {
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
    // One request for the whole kind, properties included. Asking `Versioned` for the adapters and
    // then `GetStartupValues` per adapter was around ninety requests for the four kinds a
    // subscription screen loads, and each of those booted the adapter in a child process to be
    // told its property names.
    const rows = await get<RawCatalogAdapter[]>(`/adapters/Catalog?prefix=${KIND_PREFIX[kind]}`);
    return (rows ?? []).map((r) => ({
      id: r.key,
      kind,
      // What the adapter calls itself, when its manifest says; otherwise worked out from its id.
      label: r.displayName?.trim() || displayName(r.key),
      native: r.native,
      versions: r.versions ?? [],
      props: toProps(r.startupValues),
      summary: r.summary ?? undefined,
      description: r.description ?? undefined,
      publisher: r.publisher ?? undefined,
      icon: r.icon ?? undefined,
      tags: r.tags ?? [],
      currentVersion: r.currentVersion ?? null,
      versionHistory: (r.versionHistory ?? []).map((v) => ({
        version: v.version,
        publishedOn: v.publishedOn ?? null,
        publishedBy: v.publishedBy ?? null,
        releaseNotes: v.releaseNotes ?? null,
        withdrawn: v.withdrawn ?? false,
        hasSource: v.hasSource ?? false,
        runtime: v.runtime ?? null,
      })),
    }));
  },

  getAdapterSource(adapterId: string, version: string): Promise<AdapterSourceListing> {
    const q = new URLSearchParams({ adapterId, version });
    return get<AdapterSourceListing>(`/adapters/source?${q}`);
  },

  getAdapterSourceFile(adapterId: string, version: string, path: string): Promise<AdapterSourceFile> {
    const q = new URLSearchParams({ adapterId, version, path });
    return get<AdapterSourceFile>(`/adapters/sourcefile?${q}`);
  },
} satisfies Partial<ApiClient>;
