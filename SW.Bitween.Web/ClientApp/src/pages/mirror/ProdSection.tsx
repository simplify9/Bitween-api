import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, CircleAlert, ExternalLink } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { api, type MirrorDiff, type MirrorSubscriptionView, type MirrorTest, type MirrorTestVerdict } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge, Button, FormError, InlineNotice, LoadError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { timeAgo } from "../../lib/dates";
import { whole } from "../../lib/numbers";
import { pathOf } from "../../pages";
import { BringDialog } from "./BringFromProd";
import { DiffLine, FamilyRow, VERDICT, type Entry, type Verdict } from "./Rehearsal";
import { plural, useMirror } from "./shared";

function useView(subscriptionId: number, enabled: boolean) {
  return useQuery({
    queryKey: keys.mirror.subscription(subscriptionId),
    queryFn: () => api.getMirrorSubscription(subscriptionId),
    enabled,
    staleTime: 60_000,
  });
}

/** The lines of a subscription's view that differ from prod: changed, or not on staging. */
const differing = (used: MirrorDiff[]) => used.filter((d) => d.level === "different" || d.level === "check" || d.level === "info");

/** Beside a subscription's name: how it stands against prod, which opens the section below. */
export function ProdBadge({ subscriptionId }: { subscriptionId: number }) {
  const mirror = useMirror();
  const view = useView(subscriptionId, mirror.ready);
  if (!mirror.ready || !view.data || "error" in view.data) return null;
  const v = view.data;
  const [label, tone, title]: [string, "ok" | "warn" | "neutral", string] = !v.onProd
    ? ["Only on staging", "neutral", "Prod has no subscription of this name"]
    : v.needsYou.length > 0 || v.toBring.length > 0
      ? ["Prod: to do", "warn", "Something it needs isn't on staging yet: see Against prod below"]
      : differing(v.used).length > 0
        ? ["Differs from prod", "warn", "It, or something it uses, differs from prod's: see Against prod below"]
        : ["Same as prod", "ok", "It and everything it uses are on staging as prod has them"];
  return (
    <a href="#prod" className="no-underline" title={title}>
      <Badge tone={tone}>{label}</Badge>
    </a>
  );
}

/**
 * A subscription beside prod's, at the foot of its page on a staging that mirrors prod: what differs,
 * what is left to do, and a test with prod's own traffic.
 */
export function ProdSection({ subscriptionId, name, type }: { subscriptionId: number; name: string; type: string }) {
  const mirror = useMirror();
  const view = useView(subscriptionId, mirror.ready);
  if (!mirror.ready) return null;
  return (
    <div id="prod" className="mt-5 scroll-mt-4">
      <Panel
        title="Against prod"
        description="This subscription beside prod's of the same name: what differs, what is left to do, and a test that sends prod's own recent traffic through it on staging."
      >
        {view.isPending ? (
          <LoadingBlock label="Reading prod and staging…" />
        ) : view.isError ? (
          <LoadError error={view.error} what="prod's side" onRetry={() => void view.refetch()} />
        ) : "error" in view.data ? (
          <InlineNotice>{view.data.error}</InlineNotice>
        ) : !view.data.onProd ? (
          <p className="text-[13px] text-ink-600">Prod has no subscription “{name}”, so there's nothing of prod's to compare it or test it with.</p>
        ) : (
          <div className="space-y-5 text-[13px] text-ink-800">
            <Differences view={view.data} />
            <LeftToDo view={view.data} name={name} type={type} canOperate={mirror.canOperate} />
            <TestPart subscriptionId={subscriptionId} prodUrl={view.data.prodUrl} canOperate={mirror.canOperate} />
          </div>
        )}
      </Panel>
    </div>
  );
}

function Differences({ view }: { view: MirrorSubscriptionView }) {
  const [sameOpen, setSameOpen] = useState(false);
  const changed = differing(view.used);
  const same = view.used.filter((d) => d.level === "same" || d.level === "faked");
  const prodPath = pathOf("subscription", { id: view.prodId ?? "" });
  return (
    <section aria-label="What differs">
      <h3 className="flex flex-wrap items-baseline gap-x-3 text-[13px] font-semibold text-ink-900">
        What differs
        {view.prodId && prodPath && (
          <a href={`${view.prodUrl}${prodPath}`} target="_blank" rel="noreferrer" className="inline-flex items-center gap-0.5 text-[12.5px] font-normal text-ink-500 hover:underline"
            title="Open prod's subscription, in a new tab">
            On prod <ExternalLink className="size-3" aria-hidden />
          </a>
        )}
      </h3>
      <p className="mt-0.5 text-ink-600">
        {changed.length === 0
          ? "Nothing: it and everything it uses are on staging as prod has them, with prod's values faked."
          : `${plural(changed.length, "item differs", "items differ")} from prod's, of the ${whole(view.used.length)} it uses. Prod's values are shown faked, as staging has them.`}
      </p>
      {changed.length > 0 && (
        <ul className="mt-1.5 space-y-1">
          {changed.map((d) => <DiffLine key={d.name} diff={d} />)}
        </ul>
      )}
      {same.length > 0 && (
        <div className="mt-1.5">
          <button type="button" aria-expanded={sameOpen} onClick={() => setSameOpen(!sameOpen)}
            className="flex w-full cursor-pointer items-baseline gap-1 text-left text-[12.5px] text-ink-600">
            {sameOpen ? <ChevronDown className="size-3.5 shrink-0 self-center" aria-hidden /> : <ChevronRight className="size-3.5 shrink-0 self-center" aria-hidden />}
            <span className="shrink-0 font-medium whitespace-nowrap text-ink-800">Same as prod ({same.length})</span>
            {!sameOpen && <span className="min-w-0 truncate">{same.map((d) => (d.level === "faked" ? `${d.name} (faked)` : d.name)).join(" · ")}</span>}
          </button>
          {sameOpen && (
            <ul className="mt-1 space-y-1">
              {same.map((d) => <DiffLine key={d.name} diff={d} />)}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}

function LeftToDo({ view, name, type, canOperate }: { view: MirrorSubscriptionView; name: string; type: string; canOperate: boolean }) {
  const [bringing, setBringing] = useState(false);
  const bring = view.toBring.filter((p) => p.state !== "waits");
  const waits = view.toBring.filter((p) => p.state === "waits");
  if (view.needsYou.length === 0 && view.toBring.length === 0) return null;
  return (
    <section aria-label="Left to do" className="rounded-lg border border-warn-300 bg-warn-100/60 px-3 py-2.5">
      <h3 className="flex items-center gap-1.5 text-[13px] font-semibold text-ink-900">
        <CircleAlert className="size-4 text-warn-700" aria-hidden /> Left to do
      </h3>
      <ul className="mt-1.5 space-y-1 text-ink-700">
        {view.needsYou.map((n) => (
          <li key={n.id}>
            {n.text}{" "}
            {n.action === "adapters-marketplace" ? (
              <Link to="/adapters?tab=marketplace" className="link">Open the Marketplace</Link>
            ) : n.action === "adapters-installed" ? (
              <Link to="/adapters?tab=installed" className="link">Open Adapters</Link>
            ) : null}
          </li>
        ))}
        {bring.length > 0 && (
          <li>
            Staging doesn't have everything it needs to run as on prod: {bring.map((p) => `the ${p.noun} “${p.key}”`).join(", ")}.{" "}
            {canOperate && (
              <button type="button" className="link cursor-pointer" onClick={() => setBringing(true)}>Bring {bring.length === 1 ? "it" : "them"} from prod</button>
            )}
          </li>
        )}
        {waits.map((p) => (
          <li key={`${p.section}/${p.key}`}>
            The {p.noun} “{p.key}” can't come yet. {p.reason}
          </li>
        ))}
      </ul>
      {bringing && (
        <BringDialog section="subscriptions" type={type} label="what it needs" picked={[name]} onClose={() => setBringing(false)} />
      )}
    </section>
  );
}

/** The test: prod's latest arrivals, each sent in on staging the way it came in on prod, and how they ran. */
function TestPart({ subscriptionId, prodUrl, canOperate }: { subscriptionId: number; prodUrl: string; canOperate: boolean }) {
  const queryClient = useQueryClient();
  const test = useQuery({
    queryKey: keys.mirror.test(subscriptionId),
    queryFn: () => api.getMirrorTest(subscriptionId),
    // Followed while it runs or waits its turn, and while other work holds it up.
    refetchInterval: (q) => {
      const t = q.state.data;
      return t?.state === "taking" || t?.state === "running" || t?.state === "queued" ? 2000 : t?.busy ? 5000 : false;
    },
  });
  const set = (t: MirrorTest) => queryClient.setQueryData(keys.mirror.test(subscriptionId), t);
  // A run adds exchanges, and a clear removes them: the page's runs and health above change with them.
  const changed = () => {
    void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
    void queryClient.invalidateQueries({ queryKey: keys.exchanges.all });
  };
  const state = test.data?.state;
  const was = useRef(state);
  useEffect(() => {
    if (was.current === "running" && state !== "running") changed();
    was.current = state;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state]);
  const take = useMutation({ mutationFn: () => api.takeMirrorTest(subscriptionId), onSuccess: set });
  const run = useMutation({ mutationFn: () => api.runMirrorTest(subscriptionId), onSuccess: set });
  const clear = useMutation({
    mutationFn: () => api.clearMirrorTest(subscriptionId),
    onSuccess: (t) => {
      set(t);
      changed();
    },
  });
  const [open, setOpen] = useState<string | null>(null);
  const [clearing, setClearing] = useState(false);

  return (
    <section aria-label="Test with prod's traffic" aria-live="polite">
      <h3 className="text-[13px] font-semibold text-ink-900">Test with prod's traffic</h3>
      {test.isPending ? (
        <LoadingBlock />
      ) : test.isError ? (
        <LoadError error={test.error} what="the test" onRetry={() => void test.refetch()} />
      ) : (
        <TestBody
          test={test.data}
          prodUrl={prodUrl}
          canOperate={canOperate}
          open={open}
          onToggle={(id) => setOpen(open === id ? null : id)}
          busy={{ take: take.isPending, run: run.isPending }}
          error={(take.error ?? run.error ?? clear.error)?.message}
          onTake={() => take.mutate()}
          onRun={() => run.mutate()}
          onClear={() => setClearing(true)}
        />
      )}
      {clearing && test.data && (
        <ConfirmDialog
          title="Clear what the test made?"
          body={`Removes the ${plural(test.data.exchanges, "exchange", "exchanges")} the test's runs made on staging, with their files and each arrival's results. Prod's arrivals stay, to run again, and so does how the last run went.`}
          confirmLabel="Clear"
          onConfirm={async () => {
            await clear.mutateAsync();
          }}
          onClose={() => setClearing(false)}
        />
      )}
    </section>
  );
}

/** How the arrivals last ran, once what that run made is cleared. */
function LastRun({ last }: { last: MirrorTestVerdict }) {
  const parts = (["different", "check", "same", "notRun"] as const)
    .filter((v) => last[v] > 0)
    .map((v) => `${whole(last[v])} ${v === "notRun" ? "didn't run" : VERDICT[v].label.toLowerCase()}`);
  return (
    <p className="font-medium text-ink-900">
      Ran {timeAgo(last.ranOn)}: {parts.join(", ")}. <span className="font-normal text-ink-600">What it made was cleared.</span>
    </p>
  );
}

function TestBody({ test, prodUrl, canOperate, open, onToggle, busy, error, onTake, onRun, onClear }: {
  test: MirrorTest;
  prodUrl: string;
  canOperate: boolean;
  open: string | null;
  onToggle: (id: string) => void;
  busy: { take: boolean; run: boolean };
  error?: string;
  onTake: () => void;
  onRun: () => void;
  onClear: () => void;
}) {
  const working = test.state === "taking" || test.state === "running" || test.state === "queued";
  const off = !canOperate ? "Your role can't do this on this staging" : test.busy ? `${test.busy} is under way` : working ? "It is under way" : undefined;
  const runs = new Map(test.runs.map((r) => [r.familyId, r]));
  const order: Verdict[] = ["different", "check", "same", "notRun"];
  const entries: Entry[] = test.families.map((family) => {
    const run = runs.get(family.id);
    return { family, run, verdict: (run?.verdict ?? "notRun") as Verdict };
  });
  // What differs first, once a run is over; while one runs, rows stay where they are.
  if (test.state !== "running") entries.sort((a, b) => order.indexOf(a.verdict) - order.indexOf(b.verdict));
  const count = (v: Verdict) => entries.filter((e) => e.verdict === v).length;
  const ran = test.runs.length > 0;

  return (
    <div className="mt-1 space-y-2">
      {test.state === "taking" ? (
        <p>Taking prod's latest arrivals of it. Prod is only read.</p>
      ) : test.takenOn == null ? (
        <p className="text-ink-600">
          Takes prod's latest arrivals of it - up to five of its exchanges, mostly ones that worked, each with what came in and everything it led to,
          faked - and sends each in on staging the way it came in on prod.
        </p>
      ) : test.families.length === 0 ? (
        <p className="text-ink-600">Prod had nothing of it to take{test.reason ? `: ${test.reason}` : "."}</p>
      ) : (
        <>
          <p className="text-ink-600">
            {plural(test.families.length, "arrival", "arrivals")} from prod, taken {timeAgo(test.takenOn)}. Each is sent in on staging the way it came in
            on prod, and counts as the same when every exchange it leads to ends as prod's did and sends and gets back the same.
          </p>
          {test.state === "running" ? (
            <p className="font-medium text-ink-900">Running: {whole(test.done)} of {whole(test.total)} arrivals.</p>
          ) : ran ? (
            <p className="font-medium text-ink-900">
              {test.ranOn ? `Ran ${timeAgo(test.ranOn)}: ` : "Ran: "}
              {order.filter((v) => count(v) > 0)
                .map((v) => `${whole(count(v))} ${v === "notRun" ? "didn't run" : VERDICT[v].label.toLowerCase()}`).join(", ")}
              .
            </p>
          ) : (
            test.last && <LastRun last={test.last} />
          )}
          <ul className="divide-y divide-ink-100 border-t border-ink-100">
            {entries.map((e) => (
              <FamilyRow
                key={e.family.id}
                entry={e}
                open={open === e.family.id}
                onToggle={() => onToggle(e.family.id)}
                prodUrl={prodUrl}
                items={new Map()}
                rehearsed={ran}
                pending={ran ? "It wasn't in the last run: run the test again." : "Not run yet."}
              />
            ))}
          </ul>
        </>
      )}
      {test.setupError && <InlineNotice>{test.setupError}</InlineNotice>}
      {test.state === "failed" && test.error && <InlineNotice>{test.error}</InlineNotice>}
      {test.busy && (
        <p className="text-ink-600">
          {test.state === "queued"
            ? `It waits its turn in a test of several: ${test.busy.charAt(0).toLowerCase()}${test.busy.slice(1)} is under way.`
            : `${test.busy} is under way; this test can start once it finishes.`}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-2">
        {test.families.length > 0 && (
          <Button size="sm" variant={!ran ? "primary" : undefined} busy={busy.run || test.state === "running"} disabled={!!off}
            title={off ?? "Send each arrival in on staging, then compare every exchange with prod's"} onClick={onRun}>
            {ran ? "Run again" : "Run the test"}
          </Button>
        )}
        <Button size="sm" variant={test.families.length === 0 ? "primary" : undefined} busy={busy.take || test.state === "taking"} disabled={!!off}
          title={off ?? "Take prod's latest arrivals of it again, faked; they replace these"} onClick={onTake}>
          {test.takenOn == null ? "Take prod's arrivals" : "Take them again"}
        </Button>
        {test.exchanges > 0 && (
          <Button size="sm" disabled={!!off} title={off ?? "Remove what the test made on staging"} onClick={onClear}>
            Clear test data ({whole(test.exchanges)})
          </Button>
        )}
      </div>
      <FormError>{error}</FormError>
    </div>
  );
}
