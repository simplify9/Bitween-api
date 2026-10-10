import { useEffect, useMemo, useRef, useState } from "react";
import { Combobox, ComboboxInput, ComboboxOption, ComboboxOptions } from "@headlessui/react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { Clock, CornerDownLeft, FileText, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSession } from "../../auth/useSession";
import { NAV_GROUPS } from "../../nav";
import { PAGES, type PageId } from "../../pages";
import { recentVisits } from "../../lib/recentPages";
import { subscriptionPath } from "../../lib/subscriptionPaths";
import { SUBSCRIPTION_TYPE_LABELS } from "../config/subscriptionLabels";
import { openDialogs } from "../ui/dialogStack";
import { useModalFocus } from "../ui/useModalFocus";

interface Item {
  key: string;
  section: "Recent" | "Go to" | "Create" | "Open";
  label: string;
  /** Kind or place, shown beside the label: "Scheduled job", "Configuration". */
  hint: string;
  href: string;
  /** Matched along with the label. */
  words: string;
}

/** One of the lists the pages already cache, fetched only when the session may read it. */
function useList<T>(key: readonly unknown[], fn: () => Promise<T[]>, allowed: boolean): T[] | undefined {
  return useQuery({ queryKey: key, queryFn: fn, enabled: allowed, staleTime: 60_000 }).data;
}

const SECTIONS: Item["section"][] = ["Recent", "Go to", "Create", "Open"];
const PER_KIND = 6;

/** Every word typed appears somewhere in the item. Starts-of-label rank first. */
function matches(items: Item[], query: string): Item[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return items;
  return items
    .filter((i) => terms.every((t) => `${i.label} ${i.hint} ${i.words}`.toLowerCase().includes(t)))
    .sort(
      (a, b) =>
        Number(!a.label.toLowerCase().startsWith(terms[0])) - Number(!b.label.toLowerCase().startsWith(terms[0])),
    );
}

/**
 * ⌘K: go to any page, open any configured thing by name, or start creating one.
 *
 * The sidebar holds 23 places and grows with every feature; a deployment holds hundreds of
 * subscriptions, partners and gateways. Opening "the Acme orders subscription" was sidebar → list
 * → search → click. Here it is ⌘K, "acme ord", Enter. Pages come from the page registry and the
 * session's permissions; things come from the same cached lists the pages already read.
 */
export function CommandPalette({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const { can } = useSession();
  const [query, setQuery] = useState("");
  const panel = useRef<HTMLDivElement>(null);
  useModalFocus(panel);

  // One of the stack, so Escape closes this rather than a dialog or page beneath it.
  useEffect(() => {
    const mine = Symbol("palette");
    openDialogs.push(mine);
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || openDialogs[openDialogs.length - 1] !== mine) return;
      e.stopPropagation();
      onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      openDialogs.splice(openDialogs.indexOf(mine), 1);
    };
  }, [onClose]);

  const subscriptions = useList(keys.subscriptions.cache, () => api.listSubscriptions(), can("subscriptions.view"));
  const partners = useList(keys.partners.list, () => api.listPartners(), can("partners.view"));
  const types = useList(keys.informationTypes.list, () => api.listInformationTypes(), can("documents.view"));
  const apiGateways = useList(keys.apiGateways.list, () => api.listApiGateways(), can("api-gateways.view"));
  const busGateways = useList(keys.busGateways.list, () => api.listBusGateways(), can("bus-gateways.view"));
  const dataSources = useList(keys.dataSources.list, () => api.listDataSources(), can("data-sources.view"));
  const valueSets = useList(keys.valueSets.list, () => api.listValueSets(), can("global-values.view"));
  const policies = useList(keys.retryPolicies.list, () => api.listRetryPolicies(), can("retry-policies.view"));
  const workGroups = useList(keys.workGroups.list, () => api.listWorkGroups(), can("workgroups.view"));

  const pages = useMemo<Item[]>(() => {
    const groupOf = (id: PageId) => NAV_GROUPS.find((g) => g.items.some((i) => i.id === id))?.label;
    return (Object.keys(PAGES) as PageId[])
      .filter((id) => !PAGES[id].path.includes(":"))
      .filter((id) => {
        const permission = PAGES[id].permission;
        return !permission || can(permission);
      })
      .map((id) => {
        const page = PAGES[id];
        const create = page.title.startsWith("New ");
        const parent = page.parent ? PAGES[page.parent].title : undefined;
        return {
          key: `page:${id}`,
          section: create ? ("Create" as const) : ("Go to" as const),
          label: page.title,
          hint: (create ? parent : groupOf(id)) ?? "",
          href: `/${page.path}`,
          words: (page.keywords ?? []).join(" "),
        };
      });
  }, [can]);

  const things = useMemo<Item[]>(() => {
    const of = <T,>(
      rows: T[] | undefined,
      kind: string,
      href: (row: T) => string,
      label: (row: T) => string,
      key: (row: T) => string | number,
      hint?: (row: T) => string,
    ) =>
      (rows ?? []).map((row) => ({
        key: `${kind}:${key(row)}`,
        section: "Open" as const,
        label: label(row),
        hint: hint?.(row) ?? kind,
        href: href(row),
        words: kind,
      }));
    return [
      ...of(
        subscriptions,
        "Subscription",
        (s) => subscriptionPath(s.id, s.type),
        (s) => s.name,
        (s) => s.id,
        (s) => SUBSCRIPTION_TYPE_LABELS[s.type] ?? "Subscription",
      ),
      ...of(
        partners,
        "Partner",
        (p) => `/partners/${p.id}`,
        (p) => p.name,
        (p) => p.id,
      ),
      ...of(
        types,
        "Information type",
        (t) => `/information-types/${t.id}`,
        (t) => t.name,
        (t) => t.id,
      ),
      ...of(
        apiGateways,
        "API gateway",
        (g) => `/api-gateways/${g.id}`,
        (g) => g.name,
        (g) => g.id,
      ),
      ...of(
        busGateways,
        "Bus gateway",
        (g) => `/bus-gateways/${g.id}`,
        (g) => g.name,
        (g) => g.id,
      ),
      ...of(
        dataSources,
        "Data source",
        (d) => `/data-sources/${d.id}`,
        (d) => d.name,
        (d) => d.id,
      ),
      ...of(
        valueSets,
        "Value set",
        (v) => `/global-values/${encodeURIComponent(v.id)}`,
        (v) => v.name,
        (v) => v.id,
      ),
      ...of(
        policies,
        "Retry policy",
        (p) => `/retry-policies/${p.id}`,
        (p) => p.name,
        (p) => p.id,
      ),
      ...of(
        workGroups,
        "Work group",
        (w) => `/work-groups/${w.id}`,
        (w) => w.name,
        (w) => w.id,
      ),
    ];
  }, [subscriptions, partners, types, apiGateways, busGateways, dataSources, valueSets, policies, workGroups]);

  const results = useMemo<Item[]>(() => {
    if (!query.trim()) {
      const recent = recentVisits().map((v) => ({
        key: `recent:${v.path}`,
        section: "Recent" as const,
        label: v.title,
        hint: v.kind,
        href: v.path,
        words: "",
      }));
      return [...recent, ...pages];
    }
    // Things are capped per kind, so a common word can't bury the pages under a hundred rows.
    const found = matches(things, query);
    const perKind = new Map<string, number>();
    const capped = found.filter((i) => {
      const kind = i.key.split(":")[0];
      const n = (perKind.get(kind) ?? 0) + 1;
      perKind.set(kind, n);
      return n <= PER_KIND;
    });
    return [...matches(pages, query), ...capped];
  }, [query, pages, things]);

  const go = (item: Item | null) => {
    if (!item) return;
    onClose();
    navigate(item.href);
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-ink-950/40 p-4 pt-[12vh]"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        ref={panel}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label="Search Bitween"
        className="w-full max-w-xl overflow-hidden rounded-2xl bg-white shadow-2xl outline-none"
      >
        <Combobox onChange={go}>
          <div className="flex items-center gap-2.5 border-b border-ink-100 px-4">
            <Search className="size-4 shrink-0 text-ink-500" aria-hidden />
            <ComboboxInput
              autoFocus
              aria-label="Search pages and configuration"
              placeholder="Go to a page, or find a subscription, partner, gateway…"
              className="h-12 w-full bg-transparent text-[15px] text-ink-900 placeholder:text-ink-400 focus:outline-none"
              onChange={(e) => setQuery(e.target.value)}
            />
            <kbd className="shrink-0 rounded border border-ink-200 px-1.5 py-0.5 font-mono text-[11px] text-ink-500">
              Esc
            </kbd>
          </div>
          <ComboboxOptions static className="max-h-[60vh] overflow-y-auto p-2">
            {results.length === 0 && (
              <p className="px-3 py-6 text-center text-sm text-ink-500">Nothing matches “{query}”.</p>
            )}
            {SECTIONS.map((section) => {
              const items = results.filter((r) => r.section === section);
              if (items.length === 0) return null;
              return (
                <div key={section} role="group" aria-label={section} className="mb-1">
                  <p className="px-3 pt-2 pb-1 text-[11px] font-semibold tracking-wide text-ink-500 uppercase">
                    {section}
                  </p>
                  {items.map((item) => (
                    <ComboboxOption
                      key={item.key}
                      value={item}
                      className="group flex cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-sm text-ink-800 data-focus:bg-ink-50"
                    >
                      {section === "Recent" ? (
                        <Clock className="size-4 shrink-0 text-ink-500" aria-hidden />
                      ) : section === "Create" ? (
                        <Plus className="size-4 shrink-0 text-ink-500" aria-hidden />
                      ) : (
                        <FileText className="size-4 shrink-0 text-ink-500" aria-hidden />
                      )}
                      <span className="min-w-0 flex-1 truncate">{item.label}</span>
                      <span className="shrink-0 text-[12px] text-ink-500">{item.hint}</span>
                      <CornerDownLeft
                        className="size-3.5 shrink-0 text-ink-400 opacity-0 group-data-focus:opacity-100"
                        aria-hidden
                      />
                    </ComboboxOption>
                  ))}
                </div>
              );
            })}
          </ComboboxOptions>
        </Combobox>
      </div>
    </div>
  );
}
