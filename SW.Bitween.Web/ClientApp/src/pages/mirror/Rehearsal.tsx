import { ChevronDown, ChevronRight, CircleAlert, CircleCheck, CircleX, ExternalLink, Info } from "lucide-react";
import { lazy, Suspense, useState } from "react";
import { Link } from "react-router";
import {
  type MirrorDiff,
  type MirrorFamily,
  type MirrorFamilyRun,
  type MirrorItem,
  type MirrorRun,
  type MirrorSample,
  type MirrorSampleFile,
} from "../../api";
import { Badge, LoadingBlock } from "../../components/ui/basics";
import { formatDateTime } from "../../lib/dates";
import { pathOf } from "../../pages";
import { exchangePath, itemPath, ItemLinks, OUTCOME, plural, SampleFile } from "./shared";

const FileCompare = lazy(() => import("./FileCompare"));

export type Verdict = "different" | "check" | "same" | "notRun";

export const VERDICT: Record<Verdict, { label: string; tone: "danger" | "warn" | "ok" | "neutral"; title: string }> = {
  different: { label: "Different", tone: "danger", title: "It ended another way than on prod, or what it sent or got back changed: its format, its fields, or how a value is written" },
  check: { label: "Check", tone: "warn", title: "Only values' text changed, and each kept its look. Fake data explains that when a mapping works values out; a wrong field taken doesn't" },
  same: { label: "Same", tone: "ok", title: "It ended as on prod, and sent and got back the same" },
  notRun: { label: "Not run", tone: "neutral", title: "It didn't run on staging: it says why" },
};

/** How a family came in on prod, in words. */
const ENTRY: Record<MirrorFamily["entry"], { label: string; title: string }> = {
  document: { label: "Document", title: "A document, routed to the subscriptions that want it" },
  gateway: { label: "Gateway call", title: "A partner's call through an API gateway" },
  api: { label: "Partner API call", title: "A partner's call to the partner API" },
  receiver: { label: "File received", title: "A file a receiver read" },
  aggregation: { label: "Aggregation", title: "Exchanges rolled up by an aggregation" },
  direct: { label: "Started by hand", title: "An exchange someone started" },
};

/** One of prod's families, and its run on staging when the last rehearsal ran it. */
export interface Entry {
  family: MirrorFamily;
  run: MirrorFamilyRun | undefined;
  verdict: Verdict;
}

/** The worst ending of a family's exchanges: a family that failed anywhere failed. */
function worst(outcomes: (MirrorSample["outcome"] | null)[]): MirrorSample["outcome"] | null {
  return outcomes.includes("failed") ? "failed" : outcomes.includes("badResponse") ? "badResponse" : outcomes.includes("success") ? "success" : null;
}

export function FamilyRow({ entry, open, onToggle, prodUrl, items, rehearsed, pending }: {
  entry: Entry;
  open: boolean;
  onToggle: () => void;
  prodUrl: string;
  items: Map<string, MirrorItem>;
  rehearsed: boolean;
  /** What to say of a family with no run yet, in place of the rehearsal's words. */
  pending?: string;
}) {
  const { family, run, verdict } = entry;
  const exchanges = pathOf("exchanges");
  const first = family.members[0];
  const prodEnding = worst(family.members.map((m) => m.outcome));
  const stagingEnding = run ? worst(run.members.map((m) => m.outcome)) : null;
  const name = family.door ?? first.subscription ?? first.informationType ?? "";
  const notRun = run?.notRun ?? pending ?? (rehearsed ? "It wasn't in the last rehearsal: rehearse again." : "Not rehearsed yet.");
  return (
    <li className="text-[13px]">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-1 py-1.5">
        <button
          type="button"
          aria-expanded={open}
          aria-label={`${name}, ${formatDateTime(first.startedOn)}: ${VERDICT[verdict].label}`}
          onClick={onToggle}
          className="flex min-w-0 cursor-pointer items-center gap-2 text-left"
        >
          {open ? <ChevronDown className="size-3.5 shrink-0" aria-hidden /> : <ChevronRight className="size-3.5 shrink-0" aria-hidden />}
          <Badge tone={VERDICT[verdict].tone} title={VERDICT[verdict].title}>{VERDICT[verdict].label}</Badge>
          <Badge tone="neutral" title={ENTRY[family.entry].title}>{ENTRY[family.entry].label}</Badge>
          <span className="truncate font-medium text-ink-900">{name}</span>
        </button>
        {family.partner && <span className="text-ink-600" title="The partner who called">from {family.partner}</span>}
        <span className="text-ink-500" title="When it came in on prod">{formatDateTime(first.startedOn)}</span>
        <span className="text-ink-500" title="Exchanges it led to on prod">{plural(family.members.length + family.cut, "exchange", "exchanges")}{family.cut > 0 ? "+" : ""}</span>
        <span className="flex items-center gap-1.5">
          {prodEnding && <Ending outcome={prodEnding} status={null} where="prod" />}
          <span className="text-ink-400" aria-hidden>→</span>
          {stagingEnding ? <Ending outcome={stagingEnding} status={null} where="staging" /> : <span className="text-ink-500">{run?.correlation ? "didn't finish" : "not run"}</span>}
        </span>
        <span className="ml-auto flex items-center gap-3 text-[12.5px]">
          {run?.correlation && exchanges && (
            <Link to={`${exchanges}?correlationId=${encodeURIComponent(run.correlation)}`} className="link" title="Open every exchange the rehearsal ran for it on staging">
              Staging's exchanges
            </Link>
          )}
          {exchanges && (
            <a href={`${prodUrl}${exchanges}?ids=${encodeURIComponent(family.members.map((m) => m.prodExchangeId).join(","))}`} target="_blank" rel="noreferrer"
              className="inline-flex items-center gap-0.5 text-ink-500 hover:underline" title="Open prod's exchanges, in a new tab: their real values are there">
              On prod <ExternalLink className="size-3" aria-hidden />
            </a>
          )}
        </span>
      </div>
      {open && (
        <div className="mb-3 ml-6 space-y-2 rounded-lg border border-ink-200 bg-ink-50/50 p-3">
          {run && run.members.length > 0 ? (
            <FamilyCompare family={family} run={run} prodUrl={prodUrl} items={items} />
          ) : (
            <div className="space-y-2">
              <p className="text-ink-700">{notRun}</p>
              <MembersPlain family={family} prodUrl={prodUrl} />
            </div>
          )}
        </div>
      )}
    </li>
  );
}

/** A family that didn't run: what prod ran, with its files. */
function MembersPlain({ family, prodUrl }: { family: MirrorFamily; prodUrl: string }) {
  const exchanges = pathOf("exchanges");
  return (
    <ul className="space-y-1 text-[12.5px]">
      {family.members.map((m) => (
        <li key={m.prodExchangeId} className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5">
          <span className="font-medium text-ink-800">{m.subscription ?? `Document of “${m.informationType}”`}</span>
          <Ending outcome={m.outcome} status={m.status} where="prod" />
          {m.errorTypes.length > 0 && (
            <span className="text-ink-500" title="The kinds of error prod reported. Its message isn't copied, since it can hold prod's data: open the exchange on prod to read it.">
              {m.errorTypes.join(" → ")}
            </span>
          )}
          <SampleFile label="In" what="what came in" file={m.input} />
          <SampleFile label="Sent" what="what Bitween sent" file={m.output} />
          <SampleFile label="Answer" what="what the partner answered" file={m.response} />
          {exchanges && (
            <a href={`${prodUrl}${exchanges}?ids=${encodeURIComponent(m.prodExchangeId)}`} target="_blank" rel="noreferrer"
              className="inline-flex items-center gap-0.5 text-ink-500 hover:underline" title="Open the exchange on prod, in a new tab">
              On prod <ExternalLink className="size-3" aria-hidden />
            </a>
          )}
        </li>
      ))}
    </ul>
  );
}

/** How the family came in, then each of its exchanges: prod's beside staging's, the ones that differ first. */
function FamilyCompare({ family, run, prodUrl, items }: { family: MirrorFamily; run: MirrorFamilyRun; prodUrl: string; items: Map<string, MirrorItem> }) {
  const [open, setOpen] = useState<number | null>(() => {
    const at = run.members.findIndex((m) => m.verdict === "different" || m.verdict === "check");
    return at < 0 ? null : at;
  });
  const samples = new Map(family.members.map((m) => [m.prodExchangeId, m]));
  const exchanges = pathOf("exchanges");
  const loud = run.diffs.filter((d) => d.level !== "same" || d.part === "entry");
  return (
    <div className="space-y-2">
      {loud.length > 0 && (
        <ul className="space-y-1" aria-label="How it came in">
          {loud.map((d) => <DiffLine key={`${d.part}:${d.name}`} diff={d} />)}
        </ul>
      )}
      <section aria-label="Its exchanges">
        <h4 className="text-[11.5px] font-medium uppercase tracking-wide text-ink-500">Its exchanges</h4>
        <ul className="mt-1 space-y-1">
          {run.members.map((m, i) => {
            const sample = m.prodExchangeId ? samples.get(m.prodExchangeId) : undefined;
            const v = (m.verdict ?? "notRun") as Verdict;
            const item = sample?.subscription ? items.get(`subscriptions\u0000${sample.subscription}`) : undefined;
            return (
              <li key={`${m.prodExchangeId ?? m.stagingExchangeId}:${i}`} className="rounded-md bg-white ring-1 ring-ink-100">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-2 py-1.5 text-[12.5px]">
                  <button type="button" aria-expanded={open === i} onClick={() => setOpen(open === i ? null : i)}
                    aria-label={`${m.subscription}: ${VERDICT[v].label}`} className="flex min-w-0 cursor-pointer items-center gap-2 text-left">
                    {open === i ? <ChevronDown className="size-3.5 shrink-0" aria-hidden /> : <ChevronRight className="size-3.5 shrink-0" aria-hidden />}
                    <Badge tone={VERDICT[v].tone} title={VERDICT[v].title}>{VERDICT[v].label}</Badge>
                    <span className="truncate font-medium text-ink-900">{sample && !sample.subscription ? `Document of “${m.subscription}”` : m.subscription}</span>
                  </button>
                  <span className="flex items-center gap-1.5">
                    {sample ? <Ending outcome={sample.outcome} status={sample.status} where="prod" /> : <span className="text-ink-500">not on prod</span>}
                    <span className="text-ink-400" aria-hidden>→</span>
                    {m.outcome ? <Ending outcome={m.outcome} status={m.status} where="staging" /> : <span className="text-ink-500">{m.stagingExchangeId ? "didn't finish" : "not run"}</span>}
                  </span>
                  <span className="ml-auto flex items-center gap-3">
                    {m.stagingExchangeId && <Link to={exchangePath(m.stagingExchangeId)} className="link" title="Open the exchange staging ran">Staging's exchange</Link>}
                    {m.prodExchangeId && exchanges && (
                      <a href={`${prodUrl}${exchanges}?ids=${encodeURIComponent(m.prodExchangeId)}`} target="_blank" rel="noreferrer"
                        className="inline-flex items-center gap-0.5 text-ink-500 hover:underline" title="Open prod's exchange, in a new tab: its real values are there">
                        On prod <ExternalLink className="size-3" aria-hidden />
                      </a>
                    )}
                  </span>
                </div>
                {open === i && (
                  <div className="border-t border-ink-100 px-3 py-2">
                    {m.diffs.length > 0 ? <Compare run={m} sample={sample} /> : <p className="text-ink-700">{m.notRun}</p>}
                    {item && (
                      <p className="mt-2 flex items-center gap-2 text-[12.5px] text-ink-500">
                        The subscription: <ItemLinks section="subscriptions" item={item} prodUrl={prodUrl} />
                      </p>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      </section>
    </div>
  );
}

function Ending({ outcome, status, where }: { outcome: MirrorSample["outcome"]; status: number | null; where: "prod" | "staging" }) {
  const o = OUTCOME[outcome];
  return (
    <Badge tone={o.tone} title={`On ${where}: ${o.title.toLowerCase()}`}>
      {o.label}
      {status != null && ` ${status}`}
    </Badge>
  );
}

// ------------------------------------------------------------------ one exchange, prod beside staging

const LEVEL = {
  different: { icon: CircleX, tone: "text-danger-600", label: "Different" },
  check: { icon: CircleAlert, tone: "text-warn-600", label: "Check" },
  info: { icon: Info, tone: "text-ink-500", label: "Differs, which explains rather than counts" },
  faked: { icon: CircleCheck, tone: "text-ok-600", label: "Same, with faked values" },
  same: { icon: CircleCheck, tone: "text-ok-600", label: "Same" },
} as const;

/** What changed first; what is the same as prod folded into one line. */
function Compare({ run, sample }: { run: MirrorRun; sample: MirrorSample | undefined }) {
  const [sameOpen, setSameOpen] = useState(false);
  const changed = run.diffs.filter((d) => d.level === "different" || d.level === "check" || d.level === "info");
  const same = run.diffs.filter((d) => d.level === "same" || d.level === "faked");
  const headline =
    run.diffs.find((d) => d.part === "result" && d.note)?.note ??
    (run.verdict === "same" ? "It went as on prod." : run.verdict === "check" ? "Only values' text changed, and each kept its look." : "Something it sent or got back changed.");
  return (
    <div className="space-y-2">
      <p className="font-medium text-ink-900">{headline}</p>
      {changed.length > 0 && (
        <section aria-label="What changed">
          <h4 className="text-[11.5px] font-medium uppercase tracking-wide text-ink-500">What changed</h4>
          <ul className="mt-1 space-y-1">
            {changed.map((d) => <DiffLine key={`${d.part}:${d.name}`} diff={d} run={run} sample={sample} />)}
          </ul>
        </section>
      )}
      {same.length > 0 && (
        <section aria-label="Same as prod">
          <button type="button" aria-expanded={sameOpen} onClick={() => setSameOpen(!sameOpen)}
            className="flex cursor-pointer items-baseline gap-1 text-left text-[12.5px] text-ink-600">
            {sameOpen ? <ChevronDown className="size-3.5 shrink-0 self-center" aria-hidden /> : <ChevronRight className="size-3.5 shrink-0 self-center" aria-hidden />}
            <span className="font-medium text-ink-800">Same as prod ({same.length})</span>
            {!sameOpen && <span className="min-w-0 truncate">{same.map((d) => (d.level === "faked" ? `${d.name} (faked)` : d.name)).join(" · ")}</span>}
          </button>
          {sameOpen && (
            <ul className="mt-1 space-y-1">
              {same.map((d) => <DiffLine key={`${d.part}:${d.name}`} diff={d} run={run} sample={sample} />)}
            </ul>
          )}
        </section>
      )}
    </div>
  );
}

/** The files of one part of the exchange, prod's (faked) and staging's. */
function filesOf(diff: MirrorDiff, run: MirrorRun | undefined, sample: MirrorSample | undefined): { prod: MirrorSampleFile | null; staging: MirrorSampleFile | null } | null {
  if (diff.part === "output") return { prod: sample?.output ?? null, staging: run?.output ?? null };
  if (diff.part === "answer") return { prod: sample?.response ?? null, staging: run?.response ?? null };
  if (diff.part === "input") return { prod: sample?.input ?? null, staging: null };
  return null;
}

export function DiffLine({ diff, run, sample }: { diff: MirrorDiff; run?: MirrorRun; sample?: MirrorSample }) {
  const [open, setOpen] = useState(false);
  const [comparing, setComparing] = useState(false);
  const files = filesOf(diff, run, sample);
  const fields = diff.fields ?? [];
  const canOpen = fields.length > 0 || !!files?.prod?.url || !!files?.staging?.url || !!diff.note;
  const level = LEVEL[diff.level];
  const Icon = level.icon;
  const staging = diff.section ? itemPath(diff.section, diff.stagingId, diff.type) : null;
  return (
    <li className="rounded-md bg-white px-2 py-1.5 text-[12.5px] ring-1 ring-ink-100">
      <button type="button" aria-expanded={open} disabled={!canOpen} onClick={() => setOpen(!open)}
        className="flex w-full cursor-pointer flex-wrap items-baseline gap-x-2 text-left disabled:cursor-default">
        <Icon className={`size-3.5 shrink-0 self-center ${level.tone}`} aria-label={level.label} />
        <span className="font-medium text-ink-900">{diff.name}</span>
        <span className="min-w-0 text-ink-700">
          {diff.prod != null && diff.prod !== diff.staging ? <>{diff.prod} <span className="text-ink-400">→</span> {diff.staging}</> : diff.staging}
        </span>
        {canOpen && (open ? <ChevronDown className="ml-auto size-3.5 shrink-0 self-center text-ink-400" aria-hidden /> : <ChevronRight className="ml-auto size-3.5 shrink-0 self-center text-ink-400" aria-hidden />)}
      </button>
      {open && (
        <div className="mt-1.5 space-y-2 pl-5">
          {diff.note && <p className="text-ink-600">{diff.note}</p>}
          {fields.length > 0 && <Fields diff={diff} />}
          {(files || staging) && (
            <p className="flex flex-wrap items-center gap-x-3 gap-y-1">
              {files?.prod?.url && <a href={files.prod.url} target="_blank" rel="noreferrer" className="link" title="Open prod's file as staging keeps it, faked, in a new tab">Prod's file</a>}
              {files?.staging?.url && <a href={files.staging.url} target="_blank" rel="noreferrer" className="link" title="Open the file staging's exchange made, in a new tab">Staging's file</a>}
              {files?.prod?.url && files.staging?.url && (
                <button type="button" className="link cursor-pointer" onClick={() => setComparing(!comparing)}>
                  {comparing ? "Hide the side by side" : "Compare side by side"}
                </button>
              )}
              {staging && <Link to={staging} className="link" title="Open it on staging">On staging</Link>}
            </p>
          )}
          {comparing && files?.prod?.url && files.staging?.url && (
            <Suspense fallback={<LoadingBlock label="Opening the files…" />}>
              <FileCompare prodUrl={files.prod.url} stagingUrl={files.staging.url} format={files.staging.format ?? files.prod.format} />
            </Suspense>
          )}
        </div>
      )}
    </li>
  );
}

/** Field by field: where, prod's value (faked) and staging's. */
function Fields({ diff }: { diff: MirrorDiff }) {
  const fields = diff.fields ?? [];
  const calls = diff.part === "calls";
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-[12px]">
        <thead className="text-[11px] text-ink-500">
          <tr>
            <th className="py-0.5 pr-3 font-medium" title={calls ? "The call" : "Where in the file or the item"}>{calls ? "Call" : "Where"}</th>
            {!calls && <th className="py-0.5 pr-3 font-medium" title="Prod's value, as staging has it: faked">Prod (faked)</th>}
            <th className="py-0.5 font-medium" title={calls ? "What staging did with it" : "Staging's value"}>{calls ? "What happened" : "Staging"}</th>
          </tr>
        </thead>
        <tbody className="align-top">
          {fields.map((f, i) => {
            const Icon = LEVEL[f.level].icon;
            return (
              <tr key={`${f.path}:${i}`} className="border-t border-ink-100">
                <td className="py-1 pr-3 font-mono text-ink-800">
                  <span className="inline-flex items-start gap-1">
                    <Icon className={`mt-0.5 size-3 shrink-0 ${LEVEL[f.level].tone}`} aria-label={LEVEL[f.level].label} />
                    <span className="break-all">{f.path}</span>
                  </span>
                </td>
                {!calls && <td className="py-1 pr-3 font-mono break-all text-ink-700">{f.prod}</td>}
                <td className="py-1 font-mono break-all text-ink-900">{f.staging}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
