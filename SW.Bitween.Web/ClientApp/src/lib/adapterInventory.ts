import type { AdapterInfo, AdapterKind, AdapterVersion, SubscriptionInfo } from "../api";

export const ADAPTER_KINDS: AdapterKind[] = ["receiver", "validator", "mapper", "handler"];

/** One adapter as the Adapters page shows it: every kind it serves, gathered from the per-kind catalogs. */
export interface InventoryAdapter {
  id: string;
  label: string;
  native: boolean;
  kinds: AdapterKind[];
  summary?: string;
  description?: string;
  publisher?: string;
  icon?: string;
  tags: string[];
  props: AdapterInfo["props"];
  currentVersion: string | null;
  /** Newest first. Built from the catalog when there is one, otherwise from the bare version list. */
  versions: AdapterVersion[];
  /** False for a package published before manifests: no history beyond its version files. */
  hasCatalog: boolean;
}

/**
 * The catalog is asked one kind at a time, and an adapter that serves two kinds comes back
 * twice. Folded into one entry per adapter id, keeping every kind it was listed under.
 */
export function mergeCatalogs(byKind: Partial<Record<AdapterKind, AdapterInfo[]>>): InventoryAdapter[] {
  const merged = new Map<string, InventoryAdapter>();
  for (const kind of ADAPTER_KINDS) {
    for (const a of byKind[kind] ?? []) {
      const existing = merged.get(a.id.toLowerCase());
      if (existing) {
        if (!existing.kinds.includes(kind)) existing.kinds.push(kind);
        continue;
      }
      const history = a.versionHistory.length > 0;
      merged.set(a.id.toLowerCase(), {
        id: a.id,
        label: a.label,
        native: a.native,
        kinds: [kind],
        summary: a.summary,
        description: a.description,
        publisher: a.publisher,
        icon: a.icon,
        tags: a.tags,
        props: a.props,
        currentVersion: a.currentVersion ?? (a.native ? null : (a.versions.at(-1) ?? null)),
        versions: (history
          ? a.versionHistory
          : a.versions.map((v) => ({ version: v, publishedOn: null, publishedBy: null, releaseNotes: null, withdrawn: false }))
        )
          .slice()
          .reverse(),
        hasCatalog: history,
      });
    }
  }
  return [...merged.values()].sort((x, y) => x.label.localeCompare(y.label));
}

export interface AdapterUsage {
  /** Subscriptions running it at all. */
  subscriptions: { id: number; name: string }[];
  /** How many of them follow the current version. */
  followingCurrent: number;
  /** How many are pinned to each version. */
  pinned: Record<string, number>;
}

/** Who runs each adapter, keyed by lower-case adapter id. A subscription using it in two slots counts once. */
export function usageByAdapter(subscriptions: SubscriptionInfo[]): Map<string, AdapterUsage> {
  const usage = new Map<string, AdapterUsage>();
  for (const s of subscriptions) {
    const seen = new Set<string>();
    for (const use of s.adapterUses ?? []) {
      const key = use.adapterId.toLowerCase();
      const entry = usage.get(key) ?? { subscriptions: [], followingCurrent: 0, pinned: {} };
      if (use.version) entry.pinned[use.version] = (entry.pinned[use.version] ?? 0) + 1;
      else entry.followingCurrent++;
      if (!seen.has(key)) {
        seen.add(key);
        entry.subscriptions.push({ id: s.id, name: s.name });
      }
      usage.set(key, entry);
    }
  }
  return usage;
}

/** Case-insensitive match on the name, id, summary, publisher and tags. */
export function matchesSearch(a: InventoryAdapter, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return [a.label, a.id, a.summary, a.publisher, ...a.tags].some((v) => v?.toLowerCase().includes(q));
}
