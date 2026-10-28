import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CircleAlert, CircleCheck, Download } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router";
import {
  api,
  type MirrorBring,
  type MirrorBringSection,
  type MirrorItemState,
  type MirrorNote,
  type MirrorPiece,
  type MirrorPieceState,
  type MirrorProdCounts,
  type MirrorProdItem,
} from "../../api";
import { keys } from "../../api/queryKeys";
import { Sparkline } from "../../components/charts/Sparkline";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Dialog } from "../../components/ui/overlays";
import { SearchBox } from "../../components/ui/SearchBox";
import { timeAgo } from "../../lib/dates";
import { whole } from "../../lib/numbers";
import { FillIns, itemPath, NoteLinks, plural, useMirror } from "./shared";

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
  const mirror = useMirror();
  const ready = mirror.ready && mirror.canOperate;
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

/**
 * The picker, then what comes, then what was brought. Given `picked`, it opens on what comes with
 * those, and going back closes it.
 */
export function BringDialog({ section, type, label, picked: given, onClose }: {
  section: MirrorBringSection;
  type?: string;
  label: string;
  picked?: string[];
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const list = useQuery({ queryKey: keys.mirror.prod(section, type), queryFn: () => api.getMirrorProd(section, type), enabled: !given });
  const [picked, setPicked] = useState<Set<string>>(() => new Set(given ?? []));
  const [shown, setShown] = useState<MirrorBring | null>(null);
  const preview = useMutation({ mutationFn: () => api.bringFromProd(section, [...picked], false), onSuccess: setShown });
  // Asked for once, when it opens on what comes.
  const started = useRef(false);
  useEffect(() => {
    if (!given || started.current) return;
    started.current = true;
    preview.mutate();
  }, [given, preview]);
  const bring = useMutation({
    mutationFn: () => api.bringFromProd(section, [...picked], true),
    onSuccess: (result) => {
      setShown(result);
      // A bring touches several kinds at once: every list may show something new.
      void queryClient.invalidateQueries();
    },
  });

  const title = `Bring ${label} from prod`;
  const chosen = { section, keys: picked };
  if (shown?.applied) return <Dialog title={title} onClose={onClose} wide><Brought result={shown} picked={chosen} onClose={onClose} /></Dialog>;
  if (shown)
    return (
      <Dialog title={title} onClose={onClose} wide>
        <Preview
          result={shown}
          picked={chosen}
          busy={bring.isPending}
          error={bring.error?.message}
          onBack={given ? onClose : () => setShown(null)}
          onBring={() => bring.mutate()}
        />
      </Dialog>
    );
  if (given)
    return (
      <Dialog title={title} onClose={onClose} wide>
        {preview.isError ? <FormError>{preview.error.message}</FormError> : <LoadingBlock label="Working out what comes…" />}
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

/** What was picked to bring: what a bring says came of itself, and what came with it. */
type Picked = { section: MirrorBringSection; keys: Set<string> };

function Preview({
  result,
  picked,
  busy,
  error,
  onBack,
  onBring,
}: {
  result: MirrorBring;
  picked: Picked;
  busy: boolean;
  error?: string;
  onBack: () => void;
  onBring: () => void;
}) {
  const doing = result.pieces.filter((p) => p.state === "new" || p.state === "adds" || p.state === "install").length;
  const kept = result.pieces.filter((p) => p.state === "kept");
  const needsYou = result.needsYou.filter((n) => n.group === "needsYou");
  const fill = result.needsYou.filter((n) => n.group === "fill");
  const faked = result.needsYou.find((n) => n.group === "faked");
  const asIs = result.needsYou.filter((n) => n.group === "asIs");
  return (
    <div className="space-y-4">
      {doing === 0 && !result.pieces.some((p) => p.state === "waits") && (
        <p className="text-sm text-ink-700">Staging already has everything these need. Nothing comes.</p>
      )}
      {needsYou.length > 0 && (
        <section className="rounded-lg border border-warn-300 bg-warn-100/60 px-3 py-2.5">
          <h3 className="flex items-center gap-1.5 text-sm font-semibold text-ink-900">
            <CircleAlert className="size-4 text-warn-700" aria-hidden /> Needs you first
          </h3>
          <ul className="mt-1.5 space-y-1 text-sm text-ink-700">
            {needsYou.map((n) => (
              <li key={n.id}>
                {n.text} <NoteLinks note={n} />
              </li>
            ))}
          </ul>
        </section>
      )}
      {PIECES.map(({ state, title, description }) => (
        <PieceGroup key={state} title={title} description={description} pieces={result.pieces.filter((p) => p.state === state)} picked={picked}
          open={state === "waits"} />
      ))}
      {fill.length > 0 && (
        <section>
          <h3
            className="text-sm font-semibold text-ink-900"
            title="Prod's addresses and secrets never come: addresses arrive as placeholders that lead nowhere (set-me.invalid), and secrets empty. Each subscription's Against prod section links to where to fill them in."
          >
            To fill in after bringing{" "}
            <span className="font-normal text-ink-500">({plural(new Set(fill.map((n) => `${n.section}/${n.key}`)).size, "item", "items")})</span>
          </h3>
          <div className="mt-1.5"><FillIns notes={fill} /></div>
        </section>
      )}
      {faked && <p className="text-[12.5px] text-ink-600">{faked.text}</p>}
      {asIs.length > 0 && (
        <section>
          <h3
            className="text-sm font-semibold text-ink-900"
            title="These settings decide how a flow behaves, so they come as prod has them. A secret is told by its name and its value; one whose name and value say nothing would come too. Clear any such one on staging after bringing."
          >
            Copied as-is from prod{" "}
            <span className="font-normal text-ink-500">
              ({plural(asIs.length, "setting", "settings")} in {plural(new Set(asIs.map((n) => `${n.section}/${n.key}`)).size, "item", "items")})
            </span>
          </h3>
          <p className="text-[12.5px] text-ink-600">Check none of these is a secret or someone's data.</p>
          <div className="mt-1.5"><AsIs notes={asIs} /></div>
        </section>
      )}
      <PieceGroup title="Already on staging, kept as it is" description="What staging has already: none of it changes." pieces={kept} picked={picked} />
      <p className="text-[12.5px] text-ink-500">
        Subscriptions arrive paused, and bus gateways and data sources switched off. Nothing on staging is overwritten or removed.
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

/** How many items' settings show before "Show all". */
const FEW = 5;

/** The settings each item brings as prod has them, one row an item, every setting with its value. */
function AsIs({ notes }: { notes: MirrorNote[] }) {
  const [all, setAll] = useState(false);
  const items = useMemo(() => {
    const byItem = new Map<string, { note: MirrorNote; settings: MirrorNote[] }>();
    for (const n of notes) {
      const id = `${n.section}/${n.key}`;
      byItem.set(id, { note: byItem.get(id)?.note ?? n, settings: [...(byItem.get(id)?.settings ?? []), n] });
    }
    return [...byItem.values()];
  }, [notes]);
  return (
    <div>
      <ul className="divide-y divide-ink-100 rounded-lg border border-ink-100 bg-white">
        {(all ? items : items.slice(0, FEW)).map(({ note, settings }) => (
          <li key={`${note.section}/${note.key}`} className="px-3 py-2 text-sm">
            <span className="font-medium text-ink-900">{note.key}</span> <span className="text-[12.5px] text-ink-500">{note.noun}</span>
            <dl className="mt-1 grid grid-cols-[minmax(0,14rem)_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-[12.5px]">
              {settings.map((s) => (
                <div key={s.id} className="contents">
                  <dt className="truncate text-ink-600" title={s.field ?? undefined}>{s.field}</dt>
                  <dd className="truncate font-mono text-ink-800" title={s.text}>{s.text}</dd>
                </div>
              ))}
            </dl>
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

/** "retry policy" as "retry policies", "partner" as "partners", as the server says them. */
const nouns = (noun: string) =>
  /[^aeiou]y$/.test(noun) ? `${noun.slice(0, -1)}ies` : /(s|x|ch|sh)$/.test(noun) ? `${noun}es` : `${noun}s`;

/** What runs, ahead of what it uses. */
const FIRST = ["subscriptions", "apiGateways", "busGateways"];

/**
 * "5 subscriptions (2 picked, 3 they need) · 1 API gateway · 3 partners": what runs first, the rest in
 * the order the pieces come.
 */
function byKind(pieces: MirrorPiece[], picked: Picked) {
  const counts = new Map<string, { noun: string; n: number; rank: number }>();
  for (const p of pieces) {
    const kind = counts.get(p.section) ?? { noun: p.noun, n: 0, rank: FIRST.includes(p.section) ? FIRST.indexOf(p.section) : FIRST.length };
    kind.n++;
    counts.set(p.section, kind);
  }
  const chosen = pieces.filter((p) => p.section === picked.section && picked.keys.has(p.key)).length;
  return [...counts]
    .sort(([, a], [, b]) => a.rank - b.rank)
    .map(([section, k]) => plural(k.n, k.noun, nouns(k.noun)) + (section === picked.section && chosen > 0 && chosen < k.n ? ` (${whole(chosen)} picked, ${whole(k.n - chosen)} they need)` : ""))
    .join(" · ");
}

/** Some of what a bring touches, counted by kind, each one behind "Show them". */
function PieceGroup({ title, description, pieces, picked, open = false, linked = false }: {
  title: string;
  description: string;
  pieces: MirrorPiece[];
  picked: Picked;
  open?: boolean;
  linked?: boolean;
}) {
  if (pieces.length === 0) return null;
  return (
    <section>
      <h3 className="text-sm font-semibold text-ink-900" title={description}>
        {title} <span className="font-normal text-ink-500">({whole(pieces.length)})</span>
      </h3>
      <details open={open} className="group">
        <summary className="cursor-pointer text-[12.5px] text-ink-600 marker:text-ink-400" title="Show, or hide, each of them and why it comes">
          {byKind(pieces, picked)} <span className="link ml-1 group-open:hidden">Show them</span>
        </summary>
        <PieceList pieces={pieces} linked={linked} />
      </details>
    </section>
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

function Brought({ result, picked, onClose }: { result: MirrorBring; picked: Picked; onClose: () => void }) {
  const failed = new Set(result.failed.map((f) => `${f.section}/${f.key}`));
  const done = result.pieces.filter((p) => (p.state === "new" || p.state === "adds" || p.state === "install") && !failed.has(`${p.section}/${p.key}`));
  const fill = new Set(result.needsYou.filter((n) => n.group === "fill").map((n) => `${n.section}/${n.key}`)).size;
  return (
    <div className="space-y-4">
      {done.length > 0 && (
        <section>
          <h3 className="flex items-center gap-1.5 text-sm font-semibold text-ink-900">
            <CircleCheck className="size-4 text-ok-600" aria-hidden /> Brought {plural(done.length, "item", "items")}
          </h3>
          <p className="text-[12.5px] text-ink-500">
            Subscriptions are paused: they run only when a test runs them.
            {fill > 0 && ` Next, fill in the addresses and secrets of ${plural(fill, "item", "items")}: each subscription's Against prod section links to them.`}
          </p>
          <details className="group">
            <summary className="cursor-pointer text-[12.5px] text-ink-600 marker:text-ink-400" title="Show, or hide, each of them, to open it">
              {byKind(done, picked)} <span className="link ml-1 group-open:hidden">Show them</span>
            </summary>
            <PieceList pieces={done} linked />
          </details>
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
