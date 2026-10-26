import { useQuery } from "@tanstack/react-query";
import { ExternalLink } from "lucide-react";
import { Link } from "react-router";
import { api, type MirrorItem, type MirrorNote, type MirrorSample, type MirrorSampleFile, type SubscriptionType } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { whole } from "../../lib/numbers";
import { subscriptionPath } from "../../lib/subscriptionPaths";
import { pathOf, type PageId } from "../../pages";

/** Whether this Bitween is a mirror whose prod is set, and what the signed-in person may do with it there. */
export function useMirror() {
  const canView = useSessionCan("mirror.view");
  const canOperate = useSessionCan("mirror.operate");
  const status = useQuery({ queryKey: keys.mirror.status, queryFn: () => api.getMirror(), enabled: canView, staleTime: 5 * 60_000 });
  return { ready: canView && !!status.data?.target && !!status.data.configured, canOperate, prodUrl: status.data?.sourceUrl ?? "" };
}

/** Where an item of each kind opens. Subscription categories have no page of their own. */
const KIND_PAGE: Record<string, PageId> = {
  workGroups: "workGroup",
  informationTypes: "informationType",
  partners: "partner",
  globalValues: "globalValueSet",
  connections: "connection",
  notificationChannels: "notificationChannel",
  retryPolicies: "retryPolicy",
  dataSources: "dataSource",
  dataSourceStatements: "dataSource",
  apiGateways: "apiGateway",
  busGateways: "busGateway",
  notifiers: "notifier",
};

/** The page an item opens on, from its id; null for a kind with no page, or an item not there. */
export function itemPath(section: string, id: string | null, type?: string | null): string | null {
  if (!id) return null;
  if (section === "subscriptions") return subscriptionPath(Number(id), (type ?? null) as SubscriptionType | null);
  const page = KIND_PAGE[section];
  return page ? pathOf(page, { id }) : null;
}

export const plural = (n: number, one: string, many: string) => `${whole(n)} ${n === 1 ? one : many}`;

/**
 * Where a note is dealt with: the adapters page; or, for an address or secret to fill in, the item on
 * staging and the same item on prod, to look its value up there.
 */
export function NoteLinks({ note }: { note: MirrorNote }) {
  const { prodUrl } = useMirror();
  const fill = note.group === "fill" && note.section;
  const staging = fill ? itemPath(note.section!, note.stagingId ?? null, note.type) : null;
  const prod = fill ? itemPath(note.section!, note.prodId ?? null, note.type) : null;
  return (
    <span className="inline-flex flex-wrap gap-x-3">
      {note.action === "adapters-marketplace" && <Link to="/adapters?tab=marketplace" className="link">Open the Marketplace</Link>}
      {note.action === "adapters-installed" && <Link to="/adapters?tab=installed" className="link">Open Adapters</Link>}
      {staging && <Link to={staging} className="link" title={`Open “${note.key}” on staging to fill it in`}>Fill it on staging</Link>}
      {prod && prodUrl && (
        <a href={`${prodUrl}${prod}`} target="_blank" rel="noreferrer" className="link inline-flex items-center gap-0.5" title={`Open “${note.key}” on prod, in a new tab, to look the value up`}>
          See it on prod <ExternalLink className="size-3" aria-hidden />
        </a>
      )}
    </span>
  );
}

/** "On staging" and "On prod" links to one item, as far as each side has it. */
export function ItemLinks({ section, item, prodUrl }: { section: string; item: MirrorItem; prodUrl: string }) {
  const staging = itemPath(section, item.stagingId, item.type);
  const prod = itemPath(section, item.prodId, item.type);
  return (
    <span className="flex items-center gap-3">
      {staging && <Link to={staging} className="link" title={`Open “${item.key}” on staging`}>On staging</Link>}
      {prod && (
        <a href={`${prodUrl}${prod}`} target="_blank" rel="noreferrer" className="link inline-flex items-center gap-0.5" title={`Open “${item.key}” on prod, in a new tab`}>
          On prod <ExternalLink className="size-3" aria-hidden />
        </a>
      )}
    </span>
  );
}

export const OUTCOME: Record<MirrorSample["outcome"], { label: string; tone: "ok" | "warn" | "danger"; title: string }> = {
  success: { label: "Worked", tone: "ok", title: "It worked" },
  badResponse: { label: "Bad answer", tone: "warn", title: "It was delivered, and the partner answered with an error" },
  failed: { label: "Failed", tone: "danger", title: "It failed" },
};

/** One of a sample's files: a link that opens staging's faked copy, or why it wasn't kept. */
export function SampleFile({ label, what, file }: { label: string; what: string; file: MirrorSampleFile | null }) {
  if (!file) return null;
  if (!file.url)
    return (
      <span className="text-ink-500" title={file.notCopied ?? undefined}>
        {label}: not kept{file.format ? ` (${file.format})` : ""}
      </span>
    );
  return (
    <a href={file.url} target="_blank" rel="noreferrer" className="link" title={`Open ${what}, faked, as staging keeps it, in a new tab`}>
      {label} ({file.format})
    </a>
  );
}

/** The exchanges page, showing one exchange. */
export function exchangePath(id: string): string {
  return `${pathOf("exchanges")}?ids=${encodeURIComponent(id)}`;
}
