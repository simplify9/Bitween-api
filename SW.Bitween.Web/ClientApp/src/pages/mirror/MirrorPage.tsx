import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CircleCheck, ChevronDown, ChevronRight, ExternalLink, GitCompareArrows, TriangleAlert } from "lucide-react";
import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import {
  api,
  type MirrorAdapter,
  type MirrorAddress,
  type MirrorCopyResult,
  type MirrorFakedKey,
  type MirrorItem,
  type MirrorKind,
  type MirrorNote,
  type MirrorParity,
  type MirrorStatus,
  type SettingRow,
  type SubscriptionType,
} from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, FormError, InlineNotice, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, PasswordInput, TextInput } from "../../components/ui/forms";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { SegmentedControl } from "../../components/ui/SegmentedControl";
import { MiniTable } from "../../components/ui/Table";
import { timeAgo } from "../../lib/dates";
import { whole } from "../../lib/numbers";
import { subscriptionPath } from "../../lib/subscriptionPaths";
import { pathOf, type PageId } from "../../pages";

const KEEP_SETTING = "Bitween.Mirror.KeepRealValues";

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
function itemPath(section: string, id: string | null, type?: string | null): string | null {
  if (!id) return null;
  if (section === "subscriptions") return subscriptionPath(Number(id), (type ?? null) as SubscriptionType | null);
  const page = KIND_PAGE[section];
  return page ? pathOf(page, { id }) : null;
}

const STATE_LABEL: Record<MirrorItem["state"], { label: string; tone: "ok" | "warn" | "danger" | "neutral"; title: string }> = {
  inSync: { label: "Same", tone: "ok", title: "Staging has it as prod has it" },
  differs: { label: "Differs", tone: "warn", title: "Staging has it, but not as prod has it. Copying updates it." },
  missing: { label: "Missing", tone: "danger", title: "Prod has it and staging doesn't. Copying creates it." },
  waiting: { label: "Waiting", tone: "warn", title: "Staging can't create it yet: it uses an adapter staging lacks" },
  extra: { label: "Only on staging", tone: "neutral", title: "Staging has it and prod doesn't" },
};

/**
 * On a staging: whether it is ready to test an upgrade on, what is left to do to get there, what the
 * test can't cover, and how staging differs from prod on purpose. Prod is only read, through its own
 * API; every value that reaches staging is faked first.
 */
export function MirrorPage() {
  const status = useQuery({ queryKey: keys.mirror.status, queryFn: () => api.getMirror() });
  const parity = useQuery({
    queryKey: keys.mirror.parity,
    queryFn: () => api.getMirrorParity(),
    enabled: status.data?.configured === true,
    staleTime: 60_000,
  });
  const [connecting, setConnecting] = useState(false);

  const header = (
    <PageHeader
      title="Prod mirror"
      description="Copies prod's setup onto this staging with every value faked, so an upgrade can be tested here before it reaches prod. Prod is only read."
      actions={
        status.data?.configured && (
          <Button size="sm" busy={parity.isFetching} onClick={() => void parity.refetch()} title="Read prod and staging again and compare them">
            Check again
          </Button>
        )
      }
      help={{
        title: "How the mirror works",
        body: (
          <>
            <p>
              Staging signs in to prod with a view-only login and reads its setup, by name: information types, partners, global
              values, subscriptions, gateways, retry policies and the rest. Nothing is installed or changed on prod.
            </p>
            <p>
              Before anything is saved here, each value is handled. <strong>How a flow behaves</strong> (formats, schedules, filters,
              mapping rules) is copied as it is. <strong>Addresses</strong> become test addresses that go nowhere.{" "}
              <strong>Secrets, partner properties and global values</strong> become fakes of the same shape. Staging never holds a
              real value of prod's.
            </p>
            <p>
              Legacy setups are copied as prod has them, such as a response handed to an Internal subscription. Bitween keeps those
              on prod but won't let anyone make a new one, so only the copy may recreate them here.
            </p>
          </>
        ),
      }}
    />
  );

  if (status.isPending) return <div>{header}<LoadingBlock /></div>;
  if (status.isError) return <div>{header}<LoadError error={status.error} what="the mirror's settings" onRetry={() => void status.refetch()} /></div>;

  const s = status.data;
  return (
    <div>
      {header}
      {!s.target && (
        <InlineNotice>
          Copying is off: this deployment isn't marked as a mirror, so nothing can be copied onto it. Checking still works. To allow
          copying, set <code className="font-mono text-[12px]">Bitween__Mirror__Target=true</code> in this staging's deployment.
        </InlineNotice>
      )}

      {!s.configured ? (
        <EmptyState
          icon={<GitCompareArrows />}
          title="Connect to prod"
          action={<Button variant="primary" onClick={() => setConnecting(true)}>Connect to prod</Button>}
        >
          Give prod's address and a view-only login. On prod, create a service account with the Viewer role and issue it a token;
          a prod on r10.0, which has no service accounts, takes a Viewer member's email and password instead.
        </EmptyState>
      ) : parity.isPending ? (
        <LoadingBlock label="Reading prod and staging… this takes a few seconds." />
      ) : parity.isError ? (
        <LoadError error={parity.error} what="the comparison" onRetry={() => void parity.refetch()} />
      ) : "error" in parity.data ? (
        <div className="space-y-3">
          <InlineNotice>{parity.data.error}</InlineNotice>
          <Button onClick={() => setConnecting(true)}>Change the connection</Button>
        </div>
      ) : (
        <Comparison parity={parity.data} status={s} onConnection={() => setConnecting(true)} />
      )}

      {connecting && <ConnectionDialog status={s} onClose={() => setConnecting(false)} />}
    </div>
  );
}

type FoldId = "setup" | "adapters" | "faked" | "addresses";

function Comparison({ parity, status, onConnection }: { parity: MirrorParity; status: MirrorStatus; onConnection: () => void }) {
  const [open, setOpen] = useState<Set<FoldId>>(new Set(["setup"]));
  const toggle = (id: FoldId) =>
    setOpen((now) => {
      const next = new Set(now);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  // A note that points at a section below opens it and takes you there.
  const show = (id: FoldId) => {
    setOpen((now) => new Set(now).add(id));
    requestAnimationFrame(() => document.getElementById(`mirror-${id}`)?.scrollIntoView?.({ behavior: "smooth", block: "start" }));
  };
  const items = new Map(parity.kinds.flatMap((k) => k.items.map((i) => [`${k.section}\u0000${i.key}`, i] as const)));
  const notes = (group: MirrorNote["group"]) => parity.notes.filter((n) => n.group === group);
  const context = { items, prodUrl: parity.prod.url, show };

  return (
    <div className="space-y-4">
      <Verdict parity={parity} />

      <div className="grid grid-cols-[minmax(0,1fr)] gap-3 md:grid-cols-2">
        <SideCard
          title="Prod"
          side={parity.prod}
          note={status.signIn === "token" ? "Read with a service account token" : "Read as a Viewer member"}
          action={<Button size="sm" onClick={onConnection}>Change</Button>}
        />
        <SideCard title="Staging (this Bitween)" side={parity.staging} note="The release being tested" />
      </div>

      <NoteGroup title="Needs you" description="Only a person can do these. Each says where." notes={notes("needsYou")} tone="warn" context={context} />

      <CopyPanel parity={parity} target={status.target} notes={notes("copy")} context={context} />

      <NoteGroup
        title="Not covered by the test"
        description="These are on prod but not on staging, or not all of them, so testing here won't show how they behave."
        notes={notes("notCovered")}
        tone="danger"
        context={context}
      />

      <NoteGroup
        title="Different on staging, on purpose"
        description="So nothing here reaches prod's partners or holds prod's data. None of this is a problem."
        notes={notes("onPurpose")}
        tone="neutral"
        context={context}
      />

      <Fold id="setup" open={open.has("setup")} onToggle={toggle} title="Prod's setup, item by item"
        summary={`${whole(parity.verdict.matching)} of ${whole(parity.verdict.total)} the same on staging`}
        description="Every kind of item, and how staging's compare with prod's. Open a kind to see which items, and open an item on staging or on prod.">
        <SetupList kinds={parity.kinds} prodUrl={parity.prod.url} />
      </Fold>

      <Fold id="adapters" open={open.has("adapters")} onToggle={toggle} title="Adapters"
        summary={parity.adapters.some((a) => a.state !== "same") ? `${parity.adapters.filter((a) => a.state !== "same").length} to look at` : "all the same"}
        description="Prod's adapters and what staging has of each. Built-in ones come with the release and are listed only when this release lacks one prod uses.">
        <AdaptersTable adapters={parity.adapters} />
      </Fold>

      <Fold id="faked" open={open.has("faked")} onToggle={toggle} title="Fake values"
        summary={`${parity.fakedKeys.filter((k) => !k.kept).length} faked, ${parity.fakedKeys.filter((k) => k.kept).length} kept`}
        description="Partner properties and global values are someone's data, so staging gets fakes of the same shape. Keep the real value only for one that is a setting, such as a time zone or a date format.">
        <KeptValues fakedKeys={parity.fakedKeys} />
      </Fold>

      <Fold id="addresses" open={open.has("addresses")} onToggle={toggle} title="Test addresses"
        summary={`${parity.addresses.length}`}
        description="Every outside address prod's setup uses, as staging has it: a test address that goes nowhere, the same one wherever prod uses the same address.">
        <AddressesTable addresses={parity.addresses} prodUrl={parity.prod.url} items={items} />
      </Fold>
    </div>
  );
}

/** The page's answer, first: ready or not, and what stands between. */
function Verdict({ parity }: { parity: MirrorParity }) {
  const v = parity.verdict;
  const left = [
    v.needsYou > 0 && `${v.needsYou} ${v.needsYou === 1 ? "thing needs" : "things need"} you`,
    v.toCopy > 0 && `${whole(v.toCopy)} ${v.toCopy === 1 ? "item is" : "items are"} left to copy`,
  ].filter(Boolean);
  return (
    <div
      role="status"
      aria-label="Readiness"
      className={`flex items-start gap-3 rounded-xl border p-4 ${v.ready ? "border-ok-200 bg-ok-50" : "border-warn-300 bg-warn-100"}`}
    >
      {v.ready ? <CircleCheck className="mt-0.5 size-6 shrink-0 text-ok-600" aria-hidden /> : <TriangleAlert className="mt-0.5 size-6 shrink-0 text-warn-700" aria-hidden />}
      <div className="min-w-0">
        <p className="text-[17px] font-semibold text-ink-900">{v.ready ? "Ready to test the upgrade" : "Not ready to test yet"}</p>
        <p className="mt-0.5 text-[13px] text-ink-700">
          Staging has {whole(v.matching)} of prod's {whole(v.total)} items as prod has them.
          {left.length > 0 && ` ${left.join(", and ")}.`}
          {v.notCovered > 0 && ` ${v.notCovered} ${v.notCovered === 1 ? "thing isn't" : "things aren't"} covered by the test; see below.`}
        </p>
        <p className="mt-1 text-[12px] text-ink-500">Checked {timeAgo(parity.checkedOn)}</p>
      </div>
    </div>
  );
}

function SideCard({ title, side, note, action }: { title: string; side: MirrorParity["prod"]; note: string; action?: ReactNode }) {
  return (
    <div className="rounded-xl border border-ink-200 bg-white p-4">
      <div className="flex items-start justify-between gap-2">
        <p className="text-[12px] font-medium uppercase tracking-wide text-ink-500">{title}</p>
        {action}
      </div>
      <p className="mt-1 truncate font-mono text-[12.5px] text-ink-800" title={side.url}>{side.url}</p>
      <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-[13px]">
        <dt className="text-ink-500">Version</dt>
        <dd className="text-ink-900" title="r10.0 builds don't report a version number">{side.version}</dd>
        <dt className="text-ink-500" title="How many exchanges it holds now">Exchanges</dt>
        <dd className="tabular-nums text-ink-900">{side.exchanges == null ? "-" : whole(side.exchanges)}</dd>
      </dl>
      <p className="mt-2 text-[12px] text-ink-500">{note}</p>
    </div>
  );
}

interface NoteContext {
  items: Map<string, MirrorItem>;
  prodUrl: string;
  show: (id: FoldId) => void;
}

const GROUP_TONE = {
  warn: "border-l-warn-400",
  danger: "border-l-danger-600",
  neutral: "border-l-ink-300",
} as const;

function NoteGroup({ title, description, notes, tone, context }: {
  title: string;
  description: string;
  notes: MirrorNote[];
  tone: keyof typeof GROUP_TONE;
  context: NoteContext;
}) {
  if (notes.length === 0) return null;
  return (
    <section aria-label={title} className={`rounded-xl border border-l-4 border-ink-200 bg-white px-4 pt-3 pb-2 ${GROUP_TONE[tone]}`}>
      <h2 className="text-[15px] font-semibold text-ink-900">
        {title} <span className="font-normal text-ink-500">({notes.length})</span>
      </h2>
      <p className="text-[13px] text-ink-500">{description}</p>
      <ul className="mt-1 divide-y divide-ink-100">
        {notes.map((n) => (
          <li key={n.id} className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 py-2 text-[13px] text-ink-800">
            <span className="min-w-0 flex-1 basis-80">{n.text}</span>
            <NoteLinks note={n} context={context} />
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Where a note's subject is: the item on staging and on prod, or the page where it is dealt with. */
function NoteLinks({ note, context }: { note: MirrorNote; context: NoteContext }) {
  const item = note.section && note.key ? context.items.get(`${note.section}\u0000${note.key}`) : undefined;
  const action =
    note.action === "adapters-marketplace" ? <Link to="/adapters?tab=marketplace" className="link">Open the Marketplace</Link>
    : note.action === "adapters-installed" ? <Link to="/adapters?tab=installed" className="link">Open Adapters</Link>
    : note.action === "faked" ? <button type="button" className="link cursor-pointer" onClick={() => context.show("faked")}>See the fake values</button>
    : note.action === "addresses" ? <button type="button" className="link cursor-pointer" onClick={() => context.show("addresses")}>See the test addresses</button>
    : null;
  if (!item && !action) return null;
  return (
    <span className="flex shrink-0 items-center gap-3 text-[12.5px]">
      {item && note.section && <ItemLinks section={note.section} item={item} prodUrl={context.prodUrl} />}
      {action}
    </span>
  );
}

/** "On staging" and "On prod" links to one item, as far as each side has it. */
function ItemLinks({ section, item, prodUrl }: { section: string; item: MirrorItem; prodUrl: string }) {
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

function CopyPanel({ parity, target, notes, context }: { parity: MirrorParity; target: boolean; notes: MirrorNote[]; context: NoteContext }) {
  const canCopy = useSessionCan("mirror.operate");
  const queryClient = useQueryClient();
  const [removeExtras, setRemoveExtras] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [result, setResult] = useState<MirrorCopyResult | null>(null);
  const extras = parity.kinds.reduce((n, k) => n + k.extra, 0);
  const toCopy = parity.verdict.toCopy;

  const copy = useMutation({
    mutationFn: () => api.copyFromProd(removeExtras),
    onSuccess: (r) => {
      setResult(r);
      queryClient.setQueryData(keys.mirror.parity, r.after);
    },
  });

  return (
    <Panel
      title="Copy prod's setup"
      description="Makes staging's setup match prod's, every value faked. Subscriptions arrive paused; data sources and bus gateways switched off."
    >
      <div className="space-y-1 text-[13px] text-ink-800">
        {notes.length === 0 ? <p>Nothing to copy: staging already has prod's setup.</p> : notes.map((n) => <p key={n.id}>{n.text}</p>)}
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-4">
        <Button
          variant="primary"
          busy={copy.isPending}
          disabled={!target || !canCopy}
          title={!target ? "This deployment isn't marked as a mirror" : !canCopy ? "Your role can't copy onto this staging" : undefined}
          onClick={() => (removeExtras ? setConfirming(true) : copy.mutate())}
        >
          {toCopy > 0 || (removeExtras && extras > 0) ? "Copy now" : "Copy again"}
        </Button>
        {extras > 0 && (
          <Checkbox
            label={`Also remove the ${whole(extras)} item(s) only staging has`}
            description="Off unless ticked, since it deletes."
            checked={removeExtras}
            onChange={(e) => setRemoveExtras(e.target.checked)}
          />
        )}
      </div>
      <FormError>{copy.error instanceof Error ? copy.error.message : ""}</FormError>
      {result && (
        <div className="mt-3 rounded-lg bg-ink-50 p-3 text-[13px] text-ink-800" aria-live="polite">
          <p>
            Copied: created {whole(result.created)}, updated {whole(result.updated)}, removed {whole(result.removed)}
            {result.adapterSteps > 0 && `, installed ${whole(result.adapterSteps)} adapter(s)`}.
            {result.failed.length === 0 && " Nothing failed."}
          </p>
          {result.failed.length > 0 && (
            <>
              <p className="mt-2 font-medium">Staging couldn't make {result.failed.length === 1 ? "this one" : `these ${result.failed.length}`}:</p>
              <ul className="mt-1 space-y-1 pl-1">
                {result.failed.map((f) => {
                  const item = context.items.get(`${f.section}\u0000${f.key}`);
                  return (
                    <li key={`${f.section}:${f.key}`} className="flex flex-wrap items-baseline gap-x-3">
                      <span>
                        {f.noun} <strong>{f.key}</strong>: {f.error}
                      </span>
                      {item && <ItemLinks section={f.section} item={item} prodUrl={context.prodUrl} />}
                    </li>
                  );
                })}
              </ul>
            </>
          )}
        </div>
      )}
      {confirming && (
        <ConfirmDialog
          title="Copy and remove"
          body={`This also removes ${whole(extras)} item(s) staging has and prod doesn't. They can't be brought back from here.`}
          confirmLabel="Copy and remove"
          onConfirm={async () => {
            await copy.mutateAsync();
          }}
          onClose={() => setConfirming(false)}
        />
      )}
    </Panel>
  );
}

function Fold({ id, open, onToggle, title, summary, description, children }: {
  id: FoldId;
  open: boolean;
  onToggle: (id: FoldId) => void;
  title: string;
  summary: string;
  description: string;
  children: ReactNode;
}) {
  return (
    <section id={`mirror-${id}`} className="scroll-mt-16 rounded-xl border border-ink-200 bg-white">
      <button
        type="button"
        aria-expanded={open}
        onClick={() => onToggle(id)}
        className="flex w-full cursor-pointer flex-wrap items-center gap-x-2 px-4 py-3 text-left"
      >
        {open ? <ChevronDown className="size-4 shrink-0 text-ink-500" aria-hidden /> : <ChevronRight className="size-4 shrink-0 text-ink-500" aria-hidden />}
        <span className="text-[15px] font-semibold text-ink-900">{title}</span>
        <span className="text-[13px] text-ink-500">{summary}</span>
      </button>
      {open && (
        <div className="px-4 pb-4">
          <p className="mb-2 text-[13px] text-ink-500">{description}</p>
          {children}
        </div>
      )}
    </section>
  );
}

function SetupList({ kinds, prodUrl }: { kinds: MirrorKind[]; prodUrl: string }) {
  const [open, setOpen] = useState<string | null>(null);
  const [all, setAll] = useState(false);
  return (
    <div className="-mx-1">
      <div className="grid grid-cols-[minmax(0,1fr)_repeat(4,3.25rem)] gap-x-2 px-1 pb-1 text-[11.5px] font-medium text-ink-500">
        <span>Kind</span>
        <span className="text-right" title="On staging as prod has it">Same</span>
        <span className="text-right" title="On staging, but not as prod has it">Differs</span>
        <span className="text-right" title="Prod has it and staging doesn't, or it waits for an adapter">Missing</span>
        <span className="text-right" title="Staging has it and prod doesn't">Only here</span>
      </div>
      {kinds.map((k) => {
        const total = k.inSync + k.differs + k.missing + k.extra;
        const isOpen = open === k.section;
        const shown = all ? k.items : k.items.filter((i) => i.state !== "inSync");
        return (
          <div key={k.section} className="border-t border-ink-100">
            <button
              type="button"
              className="grid w-full cursor-pointer grid-cols-[minmax(0,1fr)_repeat(4,3.25rem)] items-center gap-x-2 rounded px-1 py-1.5 text-left text-[13px] hover:bg-ink-50 disabled:cursor-default disabled:hover:bg-transparent"
              aria-expanded={isOpen}
              aria-label={
                k.skipped
                  ? `${k.plural}: not read from prod`
                  : `${k.plural}: ${k.inSync} same, ${k.differs} differ, ${k.missing} missing, ${k.extra} only on staging`
              }
              disabled={total === 0 && !k.skipped}
              onClick={() => setOpen(isOpen ? null : k.section)}
            >
              <span className="flex min-w-0 items-center gap-1 text-ink-800">
                {total > 0 || k.skipped ? (isOpen ? <ChevronDown className="size-3.5 shrink-0" /> : <ChevronRight className="size-3.5 shrink-0" />) : <span className="w-3.5" />}
                <span className="truncate first-letter:uppercase">{k.plural}</span>
                {k.skipped && <Badge tone="neutral" className="ml-1" title={k.skipped}>Not read</Badge>}
              </span>
              <Count n={k.inSync} tone="text-ink-600" />
              <Count n={k.differs} tone="text-warn-700" />
              <Count n={k.missing} tone="text-danger-700" />
              <Count n={k.extra} tone="text-ink-600" />
            </button>
            {isOpen && (
              <div className="mb-2 ml-6 space-y-1 text-[12.5px]">
                {k.skipped && <p className="text-ink-600">{k.skipped}</p>}
                {k.inSync > 0 && (
                  <label className="flex cursor-pointer items-center gap-1.5 text-ink-500">
                    <input type="checkbox" className="size-3.5 accent-crimson-600" checked={all} onChange={(e) => setAll(e.target.checked)} />
                    Show the {k.inSync} that are the same too
                  </label>
                )}
                {shown.length === 0 && !k.skipped && <p className="text-ink-500">All {k.inSync} are the same as on prod.</p>}
                {shown.map((i) => (
                  <div key={`${i.state}:${i.key}`} className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
                    <Badge tone={i.refused ? "danger" : STATE_LABEL[i.state].tone} title={i.refused ? "Staging tried and couldn't make it" : STATE_LABEL[i.state].title}>
                      {i.refused ? "Couldn't copy" : STATE_LABEL[i.state].label}
                    </Badge>
                    <span className="font-medium text-ink-900">{i.key}</span>
                    {i.fields.length > 0 && <span className="text-ink-500">{i.fields.join(", ")}</span>}
                    <ItemLinks section={k.section} item={i} prodUrl={prodUrl} />
                    {i.reason && <span className="basis-full text-ink-500">{i.reason}</span>}
                  </div>
                ))}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

const Count = ({ n, tone }: { n: number; tone: string }) => (
  <span className={`text-right tabular-nums ${n === 0 ? "text-ink-300" : tone}`}>{n}</span>
);

const ADAPTER_SOURCE: Record<MirrorAdapter["source"], string> = { builtin: "Built in", marketplace: "Marketplace", custom: "Custom" };

function AdaptersTable({ adapters }: { adapters: MirrorAdapter[] }) {
  return (
    <MiniTable
      rows={adapters}
      rowKey={(a) => a.id}
      empty="Staging has every adapter prod has, at the same versions."
      columns={[
        { header: "Adapter", cell: (a) => <code className="font-mono text-[12px] text-ink-800">{a.id}</code> },
        { header: "From", headerTitle: "Built into Bitween, published on the marketplace, or uploaded", cell: (a) => ADAPTER_SOURCE[a.source] },
        { header: "Prod", headerTitle: "The version prod runs; - when prod doesn't say", cell: (a) => a.prodVersion ?? "-" },
        { header: "Staging", headerTitle: "The version staging runs", cell: (a) => a.stagingVersion ?? "-" },
        {
          header: "State",
          cell: (a) => (
            <Badge tone={a.state === "same" ? "ok" : a.state === "missing" ? "danger" : "warn"}>
              {a.state === "same" ? "Same" : a.state === "missing" ? "Missing" : "Another version"}
            </Badge>
          ),
        },
        { header: "Used by", align: "right", headerTitle: "How many of prod's items use it", cell: (a) => <span className="tabular-nums">{a.usedBy}</span> },
        {
          header: "",
          align: "right",
          cell: (a) =>
            a.state === "same" || a.source === "builtin" ? null : (
              <Link to={a.source === "marketplace" ? "/adapters?tab=marketplace" : "/adapters?tab=installed"} className="link text-[12.5px]">
                {a.source === "marketplace" ? "Open the Marketplace" : "Open Adapters"}
              </Link>
            ),
        },
      ]}
    />
  );
}

function KeptValues({ fakedKeys }: { fakedKeys: MirrorFakedKey[] }) {
  const canEdit = useSessionCan("settings.edit");
  const queryClient = useQueryClient();
  const save = useMutation({
    mutationFn: (kept: string[]) => api.updateSetting(KEEP_SETTING, kept.length ? kept.join("\n") : null),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: keys.mirror.parity }),
        queryClient.invalidateQueries({ queryKey: keys.settings.all }),
      ]),
  });
  const toggle = (id: string, keep: boolean) =>
    save.mutate([...fakedKeys.filter((k) => (k.id === id ? keep : k.kept)).map((k) => k.id)]);

  return (
    <>
      <MiniTable
        rows={fakedKeys}
        rowKey={(k) => k.id}
        empty="Prod's partners and global values hold nothing to fake."
        search={{ text: (k) => k.label, noun: "values" }}
        columns={[
          { header: "Value", cell: (k) => <span className="text-ink-800">{k.label}</span> },
          { header: "Partners", align: "right", headerTitle: "How many partners have it; 1 for a global value", cell: (k) => <span className="tabular-nums">{k.count}</span> },
          {
            header: "Keep real",
            headerTitle: "Copy prod's value as it is. Addresses, secrets and values named like secrets are never kept, and aren't listed.",
            align: "right",
            cell: (k) => (
              <input
                type="checkbox"
                aria-label={`Keep the real value of ${k.label}`}
                className="size-4 cursor-pointer accent-crimson-600 disabled:cursor-not-allowed"
                checked={k.kept}
                disabled={!canEdit || save.isPending}
                title={!canEdit ? "Your role can't change settings" : undefined}
                onChange={(e) => toggle(k.id, e.target.checked)}
              />
            ),
          },
        ]}
      />
      <FormError>{save.error instanceof Error ? save.error.message : ""}</FormError>
      {fakedKeys.some((k) => k.kept) && <p className="mt-2 text-[12.5px] text-ink-500">Kept values arrive with the next copy.</p>}
    </>
  );
}

function AddressesTable({ addresses, prodUrl, items }: { addresses: MirrorAddress[]; prodUrl: string; items: Map<string, MirrorItem> }) {
  return (
    <MiniTable
      rows={addresses}
      rowKey={(a) => a.mock}
      empty="Prod's setup uses no outside address."
      search={{ text: (a) => `${a.mock} ${a.kind} ${a.usedBy.map((u) => `${u.noun} ${u.key} ${u.where}`).join(" ")}`, noun: "addresses" }}
      columns={[
        { header: "Test address", headerTitle: "Goes nowhere: the name is reserved never to resolve", cell: (a) => <code className="font-mono text-[12px] text-ink-800">{a.mock}</code> },
        { header: "Kind", headerTitle: "What prod reaches at this address: from its scheme, or the setting's name", cell: (a) => a.kind },
        {
          header: "Used by",
          cell: (a) => (
            <ul className="space-y-0.5 text-[12.5px] text-ink-700">
              {a.usedBy.map((u) => {
                const item = items.get(`${u.section}\u0000${u.key}`);
                return (
                  <li key={`${u.section}:${u.key}:${u.where}`} className="flex flex-wrap items-baseline gap-x-2">
                    <span>
                      {u.noun} <span className="font-medium">{u.key}</span>
                      <span className="text-ink-500"> · {u.where}</span>
                    </span>
                    {item && <ItemLinks section={u.section} item={item} prodUrl={prodUrl} />}
                  </li>
                );
              })}
            </ul>
          ),
        },
      ]}
    />
  );
}

type SignIn = "token" | "member";

const SETTING = {
  url: "Bitween.Mirror.SourceUrl",
  token: "Bitween.Mirror.SourceToken",
  email: "Bitween.Mirror.SourceEmail",
  password: "Bitween.Mirror.SourcePassword",
} as const;

function ConnectionDialog({ status, onClose }: { status: MirrorStatus; onClose: () => void }) {
  const canEdit = useSessionCan("settings.edit");
  const queryClient = useQueryClient();
  const settings = useQuery({ queryKey: keys.settings.list, queryFn: () => api.listSettings() });
  const row = (key: string): SettingRow | undefined => settings.data?.find((r) => r.key === key);

  const [url, setUrl] = useState(status.sourceUrl ?? "");
  const [signIn, setSignIn] = useState<SignIn>(status.signIn ?? "token");
  const [token, setToken] = useState("");
  const [email, setEmail] = useState<string | null>(null);
  const [password, setPassword] = useState("");

  const save = useMutation({
    mutationFn: async () => {
      await api.updateSetting(SETTING.url, url.trim() || null);
      if (signIn === "token") {
        if (token) await api.updateSetting(SETTING.token, token.trim());
      } else {
        // The token is used whenever one is set, so signing in as a member clears it.
        if (row(SETTING.token)?.hasValue) await api.updateSetting(SETTING.token, null);
        if (email !== null) await api.updateSetting(SETTING.email, email.trim() || null);
        if (password) await api.updateSetting(SETTING.password, password);
      }
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: keys.mirror.all }),
        queryClient.invalidateQueries({ queryKey: keys.settings.all }),
      ]);
      onClose();
    },
  });

  const saved = (key: string) => (row(key)?.hasValue ? "Saved. Type a new one to replace it." : undefined);
  return (
    <Dialog title="Connect to prod" onClose={onClose}>
      {settings.isPending ? (
        <LoadingBlock />
      ) : (
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          <Field label="Prod's address" hint="Staging reads prod through its own API, so nothing is installed or changed there.">
            <TextInput value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://bitween.acme.com" required disabled={!canEdit} />
          </Field>
          <SegmentedControl<SignIn>
            label="Sign in with"
            value={signIn}
            onChange={setSignIn}
            disabled={!canEdit}
            options={[
              { value: "token", label: "Service account token", title: "For prod on a release with service accounts" },
              { value: "member", label: "Viewer member", title: "For prod on r10.0, which has no service accounts" },
            ]}
          />
          {signIn === "token" ? (
            <Field label="Service account token" hint={saved(SETTING.token) ?? "On prod: Service accounts, add one with the Viewer role, then issue a token."}>
              <PasswordInput value={token} onChange={(e) => setToken(e.target.value)} placeholder="bwt_…" autoComplete="off" disabled={!canEdit} />
            </Field>
          ) : (
            <>
              <Field label="Viewer email" hint="A member on prod with the Viewer role.">
                <TextInput
                  value={email ?? row(SETTING.email)?.value ?? ""}
                  onChange={(e) => setEmail(e.target.value)}
                  type="email"
                  autoComplete="off"
                  disabled={!canEdit}
                />
              </Field>
              <Field label="Viewer password" hint={saved(SETTING.password)}>
                <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" disabled={!canEdit} />
              </Field>
            </>
          )}
          {!canEdit && <InlineNotice>Your role can't change settings, so the connection can't be changed here.</InlineNotice>}
          <FormError>{save.error instanceof Error ? save.error.message : ""}</FormError>
          <div className="flex justify-end gap-2">
            <Button onClick={onClose}>Cancel</Button>
            <Button type="submit" variant="primary" busy={save.isPending} disabled={!canEdit || !url.trim()}>
              Save and check
            </Button>
          </div>
        </form>
      )}
    </Dialog>
  );
}
