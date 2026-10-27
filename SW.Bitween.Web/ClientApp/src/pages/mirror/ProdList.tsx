import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FlaskConical } from "lucide-react";
import { useMemo, useState } from "react";
import { api, type MirrorBringSection, type MirrorListed, type MirrorListedItem, type MirrorListFilter, type SubscriptionType } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge, Button, FormError } from "../../components/ui/basics";
import { Dialog } from "../../components/ui/overlays";
import { FilterSelect } from "../../components/ui/SearchSelect";
import { SearchBox } from "../../components/ui/SearchBox";
import { timeAgo } from "../../lib/dates";
import { whole } from "../../lib/numbers";
import { plural, useMirror } from "./shared";

type Status = NonNullable<MirrorListedItem["verdict"]> | "untested";

const STATUS: Record<Status, { label: string; tone: "ok" | "warn" | "danger" | "neutral"; title: string }> = {
  same: { label: "Same", tone: "ok", title: "Its test with prod's traffic ended as on prod" },
  check: { label: "Check", tone: "warn", title: "Its test ran, and something needs a look: only values' text changed, or an arrival didn't run" },
  different: { label: "Different", tone: "danger", title: "Its test with prod's traffic ended another way than on prod, or sent or got back something else" },
  untested: { label: "Not tested", tone: "neutral", title: "Prod has it too, and it hasn't been tested with prod's traffic since prod's arrivals were taken" },
  noTraffic: { label: "No traffic", tone: "neutral", title: "Prod had none of its exchanges to test it with when its arrivals were last taken" },
};

/** The lists' filter on how their items stand against prod, for FilterSelect. */
export const PROD_FILTER_OPTIONS: { value: MirrorListFilter | ""; label: string }[] = [
  { value: "", label: "Prod: any" },
  { value: "fromProd", label: "From prod" },
  { value: "untested", label: "Not tested" },
  { value: "different", label: "Different from prod" },
  { value: "check", label: "To check" },
  { value: "same", label: "Same as prod" },
  { value: "noTraffic", label: "No traffic on prod" },
];

const statusOf = (item: MirrorListedItem): Status => item.verdict ?? "untested";

/**
 * On a staging that mirrors prod: the list's items that prod has too, with how their tests last ran,
 * followed while tests run. Not asked for anywhere else.
 */
export function useProdListing(section: MirrorBringSection) {
  const mirror = useMirror();
  const listed = useQuery({
    queryKey: keys.mirror.listed(section),
    queryFn: () => api.getMirrorListed(section),
    enabled: mirror.ready,
    staleTime: 30_000,
    refetchInterval: (q) => (q.state.data?.testing ? 3000 : false),
  });
  const byId = useMemo(() => new Map((listed.data?.items ?? []).map((i) => [i.id, i])), [listed.data]);
  return { ready: mirror.ready, canOperate: mirror.canOperate, data: listed.data, byId };
}

export type ProdListing = ReturnType<typeof useProdListing>;

/** A row's "Prod" cell: how its test with prod's traffic last went, or - when prod has nothing of its name. */
export function ProdStatus({ listing, id }: { listing: ProdListing; id: number }) {
  const item = listing.byId.get(id);
  if (!listing.data) return null;
  if (!item) return <span className="text-ink-400" title="Prod has nothing of this name">-</span>;
  if (listing.data.testing === item.key) return <Badge tone="neutral" title="Its test is under way">Testing…</Badge>;
  if (listing.data.waiting.includes(item.key)) return <Badge tone="neutral" title="Its test waits its turn">Waiting</Badge>;
  const status = STATUS[statusOf(item)];
  const gateway = item.type == null;
  const when = item.ranOn ? ` Ran ${timeAgo(item.ranOn)}.` : "";
  const partly = gateway && item.tested > 0 && item.tested < item.of;
  return (
    <span className="inline-flex items-center gap-1.5">
      <Badge
        tone={status.tone}
        title={gateway
          ? `${status.title}: of the ${plural(item.of, "subscription", "subscriptions")} it leads to that prod has too, ${whole(item.tested)} tested.${when}`
          : `${status.title}.${when}`}
      >
        {status.label}
      </Badge>
      {partly && <span className="text-[12px] text-ink-500 tabular-nums">{whole(item.tested)} of {whole(item.of)}</span>}
    </span>
  );
}

/**
 * Above a list on a mirror: why its Prod column is empty, when prod can't be read; or, while tests with
 * prod's traffic run, which, and how many wait, with a way to drop those.
 */
export function ProdNotice({ listing }: { listing: ProdListing }) {
  const queryClient = useQueryClient();
  const stop = useMutation({
    mutationFn: () => api.stopMirrorTests(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.mirror.all }),
  });
  const data = listing.data;
  if (data?.error) return <p className="mb-4 text-[13px] text-warn-700">The Prod column is empty: {data.error}</p>;
  if (!data?.testing) return null;
  return (
    <div role="status" className="mb-4 flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border border-ink-200 bg-ink-50 px-3 py-2 text-[13px] text-ink-800">
      <span>
        Testing “{data.testing}” with prod's traffic
        {data.waiting.length > 0 ? `; ${plural(data.waiting.length, "more waits its turn", "more wait their turn")}.` : "."}
      </span>
      {listing.canOperate && data.waiting.length > 0 && (
        <Button size="sm" busy={stop.isPending} onClick={() => stop.mutate()} title="Drop the tests waiting their turn. The one under way finishes.">
          Stop after this one
        </Button>
      )}
      <FormError>{stop.error?.message}</FormError>
    </div>
  );
}

/**
 * "Test from prod" on a list page: picks some of the items prod has too and tests them with prod's
 * traffic, one after another, as each subscription's page would. For whoever may operate the mirror.
 */
export function TestFromProd({ listing, section, type, label }: {
  listing: ProdListing;
  section: MirrorBringSection;
  type?: SubscriptionType;
  label: string;
}) {
  const [open, setOpen] = useState(false);
  const items = (listing.data?.items ?? []).filter((i) => !type || i.type === type);
  if (!listing.ready || !listing.canOperate || items.length === 0) return null;
  return (
    <>
      <Button onClick={() => setOpen(true)} title={`Test some of these ${label} with prod's own recent traffic, one after another`}>
        <FlaskConical className="size-4" /> Test from prod
      </Button>
      {open && listing.data && (
        <TestDialog section={section} label={label} items={items} listed={listing.data} onClose={() => setOpen(false)} />
      )}
    </>
  );
}

function TestDialog({ section, label, items, listed, onClose }: {
  section: MirrorBringSection;
  label: string;
  items: MirrorListedItem[];
  listed: MirrorListed;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [q, setQ] = useState("");
  const [only, setOnly] = useState<Status | "">("");
  const [picked, setPicked] = useState<Set<number>>(new Set());
  const [checked, setChecked] = useState(false);
  const test = useMutation({
    mutationFn: () => api.testMirrorMany(section, [...picked]),
    onSuccess: async () => {
      // Every list shows the test under way, and each subscription's page its own.
      await queryClient.invalidateQueries({ queryKey: keys.mirror.all });
      onClose();
    },
  });
  const visible = items.filter((i) => (!only || statusOf(i) === only) && (!q.trim() || i.key.toLowerCase().includes(q.trim().toLowerCase())));
  const allShown = visible.length > 0 && visible.every((i) => picked.has(i.id));
  const toggle = (id: number) => {
    const next = new Set(picked);
    if (!next.delete(id)) next.add(id);
    setPicked(next);
  };
  const toggleShown = () => {
    const next = new Set(picked);
    for (const i of visible) {
      if (allShown) next.delete(i.id);
      else next.add(i.id);
    }
    setPicked(next);
  };
  const gateways = section !== "subscriptions";
  // An adapter that doesn't come with Bitween can call addresses written in its own code.
  const custom = items.filter((i) => picked.has(i.id) && i.custom).map((i) => i.key);

  return (
    <Dialog title={`Test ${label} from prod`} onClose={onClose} wide>
      <div className="space-y-3">
        <p className="text-sm text-ink-600">
          Each is tested as on its page{gateways ? ", through the subscriptions it leads to" : ""}: prod's latest arrivals are taken - up to five,
          faked - unless some are already, then sent in on staging the way they came in on prod and compared. One at a time; the list shows each
          as it finishes.
        </p>
        {listed.testing && <p className="text-sm text-warn-700">The test of “{listed.testing}” is under way: these can start once it finishes.</p>}
        <div className="flex flex-wrap items-center justify-between gap-2">
          <SearchBox value={q} onChange={setQ} label={`Search ${label}`} className="w-full max-w-xs" />
          <div className="w-44">
            <FilterSelect
              aria-label="Show those whose test says"
              value={only}
              onChange={(v) => setOnly(v as Status | "")}
              options={[{ value: "", label: "Any result" }, ...(Object.keys(STATUS) as Status[]).map((s) => ({ value: s, label: STATUS[s].label }))]}
            />
          </div>
          <label className="flex cursor-pointer items-center gap-2 text-sm text-ink-700" title="Pick, or unpick, every one shown">
            <input type="checkbox" className="size-4 cursor-pointer accent-crimson-600" checked={allShown} onChange={toggleShown} />
            Pick all {visible.length === items.length ? whole(items.length) : `${whole(visible.length)} shown`}
          </label>
        </div>
        <ul className="max-h-[50vh] divide-y divide-ink-100 overflow-y-auto rounded-lg border border-ink-100">
          {visible.map((item) => {
            const status = STATUS[statusOf(item)];
            return (
              <li key={item.id}>
                <label className="flex cursor-pointer items-center gap-3 px-3 py-2.5 hover:bg-ink-50">
                  <input
                    type="checkbox"
                    className="size-4 cursor-pointer accent-crimson-600"
                    checked={picked.has(item.id)}
                    onChange={() => toggle(item.id)}
                    aria-label={`Pick “${item.key}”`}
                  />
                  <span className="min-w-0 flex-1 text-sm font-medium break-all text-ink-900">{item.key}</span>
                  {gateways && item.of === 0 ? (
                    <span className="shrink-0 text-[12.5px] text-ink-500" title="None of the subscriptions it leads to is on prod">Nothing to test</span>
                  ) : (
                    <span className="flex shrink-0 items-center gap-2 text-[12.5px] text-ink-500">
                      {item.ranOn && <span title="When its test last ran">{timeAgo(item.ranOn)}</span>}
                      <Badge tone={status.tone} title={status.title}>{status.label}</Badge>
                    </span>
                  )}
                </label>
              </li>
            );
          })}
          {visible.length === 0 && <li className="px-3 py-6 text-center text-sm text-ink-500">None matches.</li>}
        </ul>
        {custom.length > 0 && (
          <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-warn-300 bg-warn-100/60 px-3 py-2.5 text-sm text-ink-800">
            <input type="checkbox" className="mt-0.5 size-4 cursor-pointer accent-crimson-600" checked={checked} onChange={(e) => setChecked(e.target.checked)} />
            <span>
              {custom.length === 1 ? `“${custom[0]}” runs` : `${whole(custom.length)} of these run`} an adapter that doesn't come with Bitween, which can call
              addresses written in its own code: staging can't see or change those. I've checked they point at test systems, never at prod's partners.
            </span>
          </label>
        )}
        <FormError>{test.error?.message}</FormError>
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm text-ink-500">{picked.size === 0 ? "Nothing picked yet" : `${whole(picked.size)} picked`}</span>
          <div className="flex gap-2">
            <Button onClick={onClose}>Cancel</Button>
            <Button
              variant="primary"
              busy={test.isPending}
              disabled={picked.size === 0 || !!listed.testing || (custom.length > 0 && !checked)}
              onClick={() => test.mutate()}
              title="Take prod's arrivals where none are taken, then send them in, one after another"
            >
              {picked.size === 0 ? "Test" : `Test ${whole(picked.size)}`}
            </Button>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
