import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ExternalLink } from "lucide-react";
import { useMemo, useState } from "react";
import { Link } from "react-router";
import { api, type MirrorItem, type MirrorNote, type MirrorSample, type MirrorSampleFile, type SubscriptionType } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge, Button } from "../../components/ui/basics";
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

/** How many fill-in rows show before "Show all". */
const FEW = 5;

/**
 * Addresses and secrets to fill in on staging, one row an item with its fields, those that stop a test
 * first; with where to fill them in and look them up, when asked for.
 */
export function FillIns({ notes, links = false }: { notes: MirrorNote[]; links?: boolean }) {
  const [all, setAll] = useState(false);
  const { canOperate } = useMirror();
  const items = useMemo(() => {
    const byItem = new Map<string, { note: MirrorNote; fields: string[]; alerts: boolean; standIn: boolean }>();
    for (const n of notes) {
      const id = `${n.section}/${n.key}`;
      const item = byItem.get(id) ?? { note: n, fields: [], alerts: n.action === "alerts", standIn: false };
      if (n.field) item.fields.push(n.field);
      if (n.action === "filledIn") item.standIn = true;
      byItem.set(id, item);
    }
    return [...byItem.values()].sort((a, b) => Number(a.alerts) - Number(b.alerts));
  }, [notes]);
  const shown = all ? items : items.slice(0, FEW);
  return (
    <div>
      <ul className="divide-y divide-ink-100 rounded-lg border border-ink-100 bg-white">
        {shown.map(({ note, fields, alerts, standIn }) => (
          <li key={`${note.section}/${note.key}`} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm">
            <span className="min-w-0 flex-1 truncate" title={note.key ?? undefined}>
              <span className="font-medium text-ink-900">{note.key}</span> <span className="text-[12.5px] text-ink-500">{note.noun}</span>
            </span>
            <span className="text-[12.5px] text-ink-600" title={fields.join(", ")}>
              {fields.length > 3 ? `${fields.slice(0, 3).join(", ")} +${fields.length - 3}` : fields.join(", ")}
            </span>
            {alerts ? (
              <Badge tone="neutral" title="It only sends alerts: tests run without it">Alerts only</Badge>
            ) : (
              <Badge tone="warn" title="A test that needs it won't run until it is filled in">Stops tests</Badge>
            )}
            {links && <NoteLinks note={note} />}
            {links && standIn && canOperate && <FilledIn section={note.section!} itemKey={note.key!} />}
          </li>
        ))}
      </ul>
      {items.length > FEW && (
        <button type="button" className="link mt-1 text-[12.5px]" onClick={() => setAll(!all)}>
          {all ? "Show fewer" : `Show all ${whole(items.length)}`}
        </button>
      )}
    </div>
  );
}

/**
 * For a secret a bring saved as a placeholder so its subscription could be made: Bitween hides it once
 * saved, so only a person can say staging's own is in.
 */
function FilledIn({ section, itemKey }: { section: string; itemKey: string }) {
  const queryClient = useQueryClient();
  const tick = useMutation({
    mutationFn: () => api.mirrorFilledIn(section, itemKey),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.mirror.all }),
  });
  return (
    <Button size="sm" busy={tick.isPending} onClick={() => tick.mutate()}
      title={`Staging can't see a saved secret, so it can't tell this was filled in. Tick once staging's own is in “${itemKey}”.`}>
      Filled in
    </Button>
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
