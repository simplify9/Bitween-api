import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CircleAlert, CircleCheck, Download } from "lucide-react";
import { useMemo, useState } from "react";
import { Link } from "react-router";
import {
  api,
  type MirrorBring,
  type MirrorBringSection,
  type MirrorItemState,
  type MirrorPiece,
  type MirrorPieceState,
  type MirrorProdCounts,
  type MirrorProdItem,
} from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Sparkline } from "../../components/charts/Sparkline";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Dialog } from "../../components/ui/overlays";
import { SearchBox } from "../../components/ui/SearchBox";
import { timeAgo } from "../../lib/dates";
import { whole } from "../../lib/numbers";
import { itemPath, plural } from "./shared";

/** Whether staging has one of prod's items, as the picker says it. */
const ON_STAGING: Record<Exclude<MirrorItemState, "extra">, { label: string; tone: "ok" | "neutral" | "crimson" | "warn"; title: string }> = {
  inSync: { label: "On staging", tone: "ok", title: "Staging has it as prod has it." },
  differs: { label: "Differs on staging", tone: "neutral", title: "Staging has it, and it differs from prod's. Bringing it keeps staging's." },
  missing: { label: "Not on staging", tone: "crimson", title: "Bringing it copies prod's, faked." },
  waiting: { label: "Waits for an adapter", tone: "warn", title: "It can't come until staging has the adapter it runs." },
};

/** What bringing does to each item, in the order the preview lists them. */
const PIECES: { state: MirrorPieceState; title: string; description: string }[] = [
  { state: "new", title: "Comes from prod", description: "Staging doesn't have these: they come as prod has them, faked." },
  { state: "adds", title: "Gets what leads to it", description: "Staging has these, and gets only the partner or route that reaches what comes." },
  { state: "install", title: "Installed from the marketplace", description: "Adapters staging doesn't have yet." },
  { state: "waits", title: "Can't come yet", description: "These stay behind until what they need is on staging." },
];

/**
 * "Bring from prod" on a list page: on a staging that mirrors prod, for whoever may operate the
 * mirror, and only while prod has something this page lists - or prod can't be read, which the
 * picker then says. Opens the picker.
 */
export function BringFromProd({ section, type, label }: { section: MirrorBringSection; type?: string; label: string }) {
  const canOperate = useSessionCan("mirror.operate");
  const status = useQuery({ queryKey: keys.mirror.status, queryFn: () => api.getMirror(), enabled: canOperate, staleTime: 5 * 60_000 });
  const ready = canOperate && !!status.data?.target && !!status.data.configured;
  const kinds = useQuery({ queryKey: keys.mirror.prodKinds, queryFn: () => api.getMirrorProdKinds(), enabled: ready, staleTime: 60_000 });
  const [open, setOpen] = useState(false);

  if (!ready || !kinds.data) return null;
  const count = section === "subscriptions" ? (kinds.data.subscriptions[type ?? "all"] ?? 0) : kinds.data[section];
  if (!kinds.data.error && count === 0) return null;
  return (
    <>
      <Button
        onClick={() => setOpen(true)}
        title={`Bring prod's ${label} onto this staging with what they need to run. Values come faked, and nothing staging has is overwritten.`}
      >
        <Download className="size-4" /> Bring from prod
      </Button>
      {open && <BringDialog section={section} type={type} label={label} onClose={() => setOpen(false)} />}
    </>
  );
}

function BringDialog({ section, type, label, onClose }: { section: MirrorBringSection; type?: string; label: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const list = useQuery({ queryKey: keys.mirror.prod(section, type), queryFn: () => api.getMirrorProd(section, type) });
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [shown, setShown] = useState<MirrorBring | null>(null);
  const preview = useMutation({ mutationFn: () => api.bringFromProd(section, [...picked], false), onSuccess: setShown });
  const bring = useMutation({
    mutationFn: () => api.bringFromProd(section, [...picked], true),
    onSuccess: (result) => {
      setShown(result);
      // A bring touches several kinds at once: every list may show something new.
      void queryClient.invalidateQueries();
    },
  });

  const title = `Bring ${label} from prod`;
  if (shown?.applied) return <Dialog title={title} onClose={onClose} wide><Brought result={shown} onClose={onClose} /></Dialog>;
  if (shown)
    return (
      <Dialog title={title} onClose={onClose} wide>
        <Preview
          result={shown}
          busy={bring.isPending}
          error={bring.error?.message}
          onBack={() => setShown(null)}
          onBring={() => bring.mutate()}
        />
      </Dialog>
    );
  return (
    <Dialog title={title} onClose={onClose} wide>
      {list.isPending ? (
        <LoadingBlock label="Reading prod and staging…" />
      ) : list.isError ? (
        <LoadError error={list.error} what={`prod's ${label}`} onRetry={() => void list.refetch()} />
      ) : "error" in list.data ? (
        <p className="text-sm text-danger-700">{list.data.error}</p>
      ) : (
        <Picker
          items={list.data.items}
          days={list.data.days}
          trafficNote={list.data.trafficNote}
          label={label}
          picked={picked}
          onPick={setPicked}
          busy={preview.isPending}
          error={preview.error?.message}
          onCancel={onClose}
          onNext={() => preview.mutate()}
        />
      )}
    </Dialog>
  );
}

function Picker({
  items,
  days,
  trafficNote,
  label,
  picked,
  onPick,
  busy,
  error,
  onCancel,
  onNext,
}: {
  items: MirrorProdItem[];
  days: number | null;
  trafficNote: string | null;
  label: string;
  picked: Set<string>;
  onPick: (next: Set<string>) => void;
  busy: boolean;
  error?: string;
  onCancel: () => void;
  onNext: () => void;
}) {
  const [q, setQ] = useState("");
  const visible = useMemo(() => {
    const words = q.trim().toLowerCase();
    return words ? items.filter((i) => `${i.key} ${i.detail}`.toLowerCase().includes(words)) : items;
  }, [items, q]);
  const allShown = visible.length > 0 && visible.every((i) => picked.has(i.key));
  const toggle = (key: string) => {
    const next = new Set(picked);
    if (!next.delete(key)) next.add(key);
    onPick(next);
  };
  const toggleShown = () => {
    const next = new Set(picked);
    for (const i of visible) {
      if (allShown) next.delete(i.key);
      else next.add(i.key);
    }
    onPick(next);
  };

  return (
    <div className="space-y-3">
      <p className="text-sm text-ink-600">
        {days != null ? `Prod's ${label}, busiest first, with their exchanges over the last ${days} days.` : trafficNote}
      </p>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SearchBox value={q} onChange={setQ} label={`Search prod's ${label}`} className="w-full max-w-xs" />
        <label className="flex cursor-pointer items-center gap-2 text-sm text-ink-700" title="Pick, or unpick, every one the search shows">
          <input type="checkbox" className="size-4 cursor-pointer accent-crimson-600" checked={allShown} onChange={toggleShown} />
          Pick all {visible.length === items.length ? whole(items.length) : `${whole(visible.length)} shown`}
        </label>
      </div>
      <ul className="max-h-[50vh] divide-y divide-ink-100 overflow-y-auto rounded-lg border border-ink-100">
        {visible.map((item) => (
          <li key={item.key}>
            <label className="flex cursor-pointer items-start gap-3 px-3 py-2.5 hover:bg-ink-50">
              <input
                type="checkbox"
                className="mt-0.5 size-4 cursor-pointer accent-crimson-600"
                checked={picked.has(item.key)}
                onChange={() => toggle(item.key)}
                aria-label={`Pick “${item.key}”`}
              />
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-medium break-all text-ink-900">{item.key}</span>
                  {item.state !== "extra" && (
                    <Badge tone={ON_STAGING[item.state].tone} title={item.reason ?? ON_STAGING[item.state].title}>
                      {ON_STAGING[item.state].label}
                    </Badge>
                  )}
                </span>
                {item.detail && <span className="block truncate text-[12.5px] text-ink-500">{item.detail}</span>}
              </span>
              {days != null && <Traffic counts={item.traffic} days={days} name={item.key} />}
            </label>
          </li>
        ))}
        {visible.length === 0 && <li className="px-3 py-6 text-center text-sm text-ink-500">Nothing on prod matches “{q}”.</li>}
      </ul>
      <FormError>{error}</FormError>
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm text-ink-500">{picked.size === 0 ? "Nothing picked yet" : `${whole(picked.size)} picked`}</span>
        <div className="flex gap-2">
          <Button onClick={onCancel}>Cancel</Button>
          <Button
            variant="primary"
            busy={busy}
            disabled={picked.size === 0}
            onClick={onNext}
            title="See everything that comes with what you picked, before anything changes"
          >
            See what comes
          </Button>
        </div>
      </div>
    </div>
  );
}

/** One item's traffic on prod: how many, how many failed, a day-by-day line, and when the last was. */
function Traffic({ counts, days, name }: { counts: MirrorProdCounts | null; days: number; name: string }) {
  if (!counts) return <span className="shrink-0 text-[12.5px] text-ink-400">No traffic</span>;
  return (
    <span className="flex shrink-0 items-center gap-3 text-right text-[12.5px]">
      <Sparkline values={counts.daily} label={`“${name}”'s exchanges a day on prod, over the last ${days} days`} width={64} height={18} />
      <span className="w-24">
        <span className="block font-medium text-ink-900" title={`Exchanges on prod over the last ${days} days`}>
          {whole(counts.total)}
        </span>
        <span className="block text-ink-500">
          {counts.failed > 0 ? (
            <span className="text-danger-700" title={`Exchanges that failed on prod over the last ${days} days`}>
              {whole(counts.failed)} failed
            </span>
          ) : counts.lastOn ? (
            <span title="When prod last ran it, to the hour">{timeAgo(counts.lastOn)}</span>
          ) : null}
        </span>
      </span>
    </span>
  );
}

function Preview({
  result,
  busy,
  error,
  onBack,
  onBring,
}: {
  result: MirrorBring;
  busy: boolean;
  error?: string;
  onBack: () => void;
  onBring: () => void;
}) {
  const doing = result.pieces.filter((p) => p.state === "new" || p.state === "adds" || p.state === "install").length;
  const kept = result.pieces.filter((p) => p.state === "kept");
  return (
    <div className="space-y-4">
      {doing === 0 && !result.pieces.some((p) => p.state === "waits") && (
        <p className="text-sm text-ink-700">Staging already has everything these need. Nothing comes.</p>
      )}
      {result.needsYou.length > 0 && (
        <section className="rounded-lg border border-warn-300 bg-warn-100/60 px-3 py-2.5">
          <h3 className="flex items-center gap-1.5 text-sm font-semibold text-ink-900">
            <CircleAlert className="size-4 text-warn-700" aria-hidden /> Needs you first
          </h3>
          <ul className="mt-1.5 space-y-1 text-sm text-ink-700">
            {result.needsYou.map((n) => (
              <li key={n.id}>
                {n.text}{" "}
                {n.action === "adapters-marketplace" ? (
                  <Link to="/adapters?tab=marketplace" className="link">Open the Marketplace</Link>
                ) : n.action === "adapters-installed" ? (
                  <Link to="/adapters?tab=installed" className="link">Open Adapters</Link>
                ) : null}
              </li>
            ))}
          </ul>
        </section>
      )}
      {PIECES.map(({ state, title, description }) => {
        const pieces = result.pieces.filter((p) => p.state === state);
        if (pieces.length === 0) return null;
        return (
          <section key={state}>
            <h3 className="text-sm font-semibold text-ink-900">
              {title} <span className="font-normal text-ink-500">({whole(pieces.length)})</span>
            </h3>
            <p className="text-[12.5px] text-ink-500">{description}</p>
            <PieceList pieces={pieces} />
          </section>
        );
      })}
      {kept.length > 0 && (
        <details className="group">
          <summary className="cursor-pointer text-sm font-semibold text-ink-900" title="What staging has already: none of it changes">
            Already on staging, kept as it is <span className="font-normal text-ink-500">({whole(kept.length)})</span>
          </summary>
          <PieceList pieces={kept} />
        </details>
      )}
      <p className="text-[12.5px] text-ink-500">
        As on every staging copy, values come faked, subscriptions arrive paused, and bus gateways and data sources switched
        off. Nothing on staging is overwritten or removed.
      </p>
      <FormError>{error}</FormError>
      <div className="flex justify-end gap-2">
        <Button onClick={onBack}>Back</Button>
        <Button variant="primary" busy={busy} disabled={doing === 0} onClick={onBring}>
          {doing === 0 ? "Staging has all of it" : `Bring ${plural(doing, "item", "items")}`}
        </Button>
      </div>
    </div>
  );
}

function PieceList({ pieces, linked = false }: { pieces: MirrorPiece[]; linked?: boolean }) {
  return (
    <ul className="mt-1.5 divide-y divide-ink-100 rounded-lg border border-ink-100">
      {pieces.map((p) => {
        const path = linked || p.state !== "new" ? itemPath(p.section, p.stagingId, p.type) : null;
        return (
          <li key={`${p.section}/${p.key}`} className="px-3 py-2 text-sm">
            <span className="text-ink-500">{p.noun} </span>
            {path ? (
              <Link to={path} className="link font-medium" title={`Open “${p.key}” on staging`}>
                {p.key}
              </Link>
            ) : (
              <span className="font-medium text-ink-900">{p.key}</span>
            )}
            <span className="block text-[12.5px] text-ink-500">{p.reason ?? p.why}</span>
          </li>
        );
      })}
    </ul>
  );
}

function Brought({ result, onClose }: { result: MirrorBring; onClose: () => void }) {
  const failed = new Set(result.failed.map((f) => `${f.section}/${f.key}`));
  const done = result.pieces.filter((p) => (p.state === "new" || p.state === "adds" || p.state === "install") && !failed.has(`${p.section}/${p.key}`));
  return (
    <div className="space-y-4">
      {done.length > 0 && (
        <section>
          <h3 className="flex items-center gap-1.5 text-sm font-semibold text-ink-900">
            <CircleCheck className="size-4 text-ok-600" aria-hidden /> Brought {plural(done.length, "item", "items")}
          </h3>
          <p className="text-[12.5px] text-ink-500">Subscriptions are paused: they run only when a test runs them.</p>
          <PieceList pieces={done} linked />
        </section>
      )}
      {result.failed.length > 0 && (
        <section>
          <h3 className="flex items-center gap-1.5 text-sm font-semibold text-ink-900">
            <CircleAlert className="size-4 text-danger-600" aria-hidden /> Staging couldn't make {plural(result.failed.length, "item", "items")}
          </h3>
          <ul className="mt-1.5 space-y-1 text-sm text-ink-700">
            {result.failed.map((f) => (
              <li key={`${f.section}/${f.key}`}>
                The {f.noun} <span className="font-medium">{f.key}</span>: {f.error}
              </li>
            ))}
          </ul>
        </section>
      )}
      <div className="flex justify-end">
        <Button variant="primary" onClick={onClose}>Done</Button>
      </div>
    </div>
  );
}
