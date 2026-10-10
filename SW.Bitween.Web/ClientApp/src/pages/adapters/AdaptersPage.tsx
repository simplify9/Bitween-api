import { lazy, Suspense, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, FileCode, Package, Pencil, Plus, Puzzle, Search, Store, Upload } from "lucide-react";
import { api, type AdapterKind, type AdapterUploadResult } from "../../api";
import { keys } from "../../api/queryKeys";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { SegmentedControl } from "../../components/ui/SegmentedControl";
import { useSubscriptionsCache } from "../../components/config/shared";
import { formatDate } from "../../lib/dates";
import { Can, useSessionCan } from "../../auth/guards";
import {
  ADAPTER_KINDS,
  matchesSearch,
  mergeCatalogs,
  usageByAdapter,
  type AdapterUsage,
  type InventoryAdapter,
  runtimeLabel,
  runtimeOf,
} from "../../lib/adapterInventory";

// CodeMirror, its diff view and the language packs load only when someone opens source.
const AdapterSourceViewer = lazy(() => import("./AdapterSourceViewer"));


type Tab = "installed" | "marketplace";
type KindFilter = "all" | AdapterKind;

const KIND_LABEL: Record<AdapterKind, string> = {
  receiver: "Receiver",
  validator: "Validator",
  mapper: "Mapper",
  handler: "Handler",
};

/**
 * Every adapter this Bitween can run: the ones built in, and the custom packages published to it,
 * with their versions and who uses them. The marketplace — adapters that could be installed — is
 * its own tab, still to come.
 */
export function AdaptersPage() {
  const [params, setParams] = useSearchParams();
  const tab: Tab = params.get("tab") === "marketplace" ? "marketplace" : "installed";
  const selectTab = (next: Tab) =>
    setParams(
      (prev) => {
        const p = new URLSearchParams(prev);
        if (next === "installed") p.delete("tab");
        else p.set("tab", next);
        return p;
      },
      { replace: true },
    );

  return (
    <div>
      <PageHeader
        title="Adapters"
        description="What Bitween can receive with, check, transform and deliver with — built in, or published to this instance."
        actions={
          <div className="flex gap-2">
            <Can permission="adapter-source.operate">
              <UploadPackageButton />
            </Can>
            <Can permission="adapter-source.edit">
              <NewAdapterButton />
            </Can>
          </div>
        }
      />

      <div role="tablist" aria-label="Adapters" className="mb-5 flex gap-1 border-b border-ink-200">
        {(
          [
            ["installed", "Installed", Package],
            ["marketplace", "Marketplace", Store],
          ] as const
        ).map(([value, label, Icon]) => (
          <button
            key={value}
            type="button"
            role="tab"
            id={`adapters-tab-${value}`}
            aria-selected={tab === value}
            aria-controls={`adapters-panel-${value}`}
            onClick={() => selectTab(value)}
            className={`-mb-px inline-flex items-center gap-1.5 border-b-2 px-3 py-2 text-[13.5px] font-medium ${
              tab === value
                ? "border-crimson-600 text-ink-900"
                : "border-transparent text-ink-500 hover:text-ink-800"
            }`}
          >
            <Icon className="size-4" aria-hidden />
            {label}
          </button>
        ))}
      </div>

      <div role="tabpanel" id={`adapters-panel-${tab}`} aria-labelledby={`adapters-tab-${tab}`}>
        {tab === "installed" ? (
          <InstalledAdapters />
        ) : (
          <EmptyState icon={<Store />} title="The marketplace is on its way">
            Browsing and installing adapters published by Simplify9 and others will live here.
          </EmptyState>
        )}
      </div>
    </div>
  );
}

function InstalledAdapters() {
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<KindFilter>("all");
  const [runtime, setRuntime] = useState("all");

  // One request per kind, folded into one entry per adapter in `combine`, which only re-runs when
  // one of the four results actually changes.
  const catalogs = useQueries({
    queries: ADAPTER_KINDS.map((k) => ({ queryKey: keys.adapters(k), queryFn: () => api.listAdapters(k) })),
    combine: (results) => ({
      loading: results.some((r) => r.isPending),
      error: results.find((r) => r.isError)?.error ?? null,
      adapters: mergeCatalogs(Object.fromEntries(ADAPTER_KINDS.map((k, i) => [k, results[i].data ?? []]))),
    }),
  });
  const subscriptions = useSubscriptionsCache();
  const usage = useMemo(() => usageByAdapter(subscriptions.data ?? []), [subscriptions.data]);

  if (catalogs.loading) return <LoadingBlock label="Reading the adapters…" />;
  if (catalogs.error) return <p className="text-sm text-danger-700">{catalogs.error.message}</p>;

  // Offered once custom adapters run on more than one runtime; a built-in adapter has none of its own.
  const runtimes = [...new Set(catalogs.adapters.map(runtimeOf).filter((r): r is string => r !== null))].sort();
  const shown = catalogs.adapters.filter(
    (a) =>
      (kind === "all" || a.kinds.includes(kind)) &&
      (runtime === "all" || runtimeOf(a) === runtime) &&
      matchesSearch(a, query),
  );
  const builtIn = shown.filter((a) => a.native);
  const custom = shown.filter((a) => !a.native);

  return (
    <div className="space-y-6">
      <Can permission="adapter-source.edit">
        <Drafts />
      </Can>
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            aria-label="Search adapters"
            placeholder="Search by name, id, publisher or tag"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="w-full rounded-lg border border-ink-200 bg-white py-2 pr-3 pl-9 text-[13.5px] focus:border-focus-500 focus:outline-none"
          />
        </div>
        <SegmentedControl<KindFilter>
          label="Kind"
          size="sm"
          value={kind}
          onChange={setKind}
          options={[
            { value: "all", label: "All" },
            ...ADAPTER_KINDS.map((k) => ({ value: k, label: `${KIND_LABEL[k]}s` })),
          ]}
        />
        {runtimes.length > 1 && (
          <SegmentedControl<string>
            label="Runtime"
            size="sm"
            value={runtime}
            onChange={setRuntime}
            options={[{ value: "all", label: "Any runtime" }, ...runtimes.map((r) => ({ value: r, label: runtimeLabel(r) ?? r }))]}
          />
        )}
      </div>

      <AdapterSection
        title="Built-in"
        description="Shipped with Bitween and run in-process. They are updated with Bitween itself, so they have no versions of their own."
        adapters={builtIn}
        usage={usage}
        empty={query || kind !== "all" || runtime !== "all" ? "No built-in adapter matches." : "No built-in adapters."}
      />
      <AdapterSection
        title="Custom"
        description="Packages published to this instance, in .NET, Python or JavaScript and TypeScript. Each runs in its own process, and published versions can be pinned per subscription."
        adapters={custom}
        usage={usage}
        empty={
          query || kind !== "all" || runtime !== "all"
            ? "No custom adapter matches."
            : "None published yet. Write one here with New adapter, or build and publish one with the bitween CLI."
        }
      />
    </div>
  );
}

function AdapterSection({
  title,
  description,
  adapters,
  usage,
  empty,
}: {
  title: string;
  description: string;
  adapters: InventoryAdapter[];
  usage: Map<string, AdapterUsage>;
  empty: string;
}) {
  return (
    <section aria-labelledby={`section-${title}`}>
      <div className="mb-2.5">
        <h2 id={`section-${title}`} className="flex items-center gap-2 text-[15px] font-semibold text-ink-900">
          {title}
          <span className="rounded-full bg-ink-100 px-2 py-0.5 text-[11.5px] font-medium text-ink-600">{adapters.length}</span>
        </h2>
        <p className="mt-0.5 text-[12.5px] text-ink-500">{description}</p>
      </div>
      {adapters.length === 0 ? (
        <p className="rounded-xl border border-dashed border-ink-200 px-4 py-6 text-center text-[13px] text-ink-500">{empty}</p>
      ) : (
        <ul className="divide-y divide-ink-100 overflow-hidden rounded-xl border border-ink-200 bg-white">
          {adapters.map((a) => (
            <AdapterRow key={a.id} adapter={a} usage={usage.get(a.id.toLowerCase())} />
          ))}
        </ul>
      )}
    </section>
  );
}

function AdapterRow({ adapter: a, usage }: { adapter: InventoryAdapter; usage?: AdapterUsage }) {
  const [open, setOpen] = useState(false);
  const used = usage?.subscriptions.length ?? 0;
  const pinnable = a.versions.filter((v) => !v.withdrawn).length;

  return (
    <li>
      <button
        type="button"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-ink-50/60"
      >
        {open ? (
          <ChevronDown className="mt-2 size-4 shrink-0 text-ink-400" aria-hidden />
        ) : (
          <ChevronRight className="mt-2 size-4 shrink-0 text-ink-400" aria-hidden />
        )}
        <AdapterIcon adapter={a} />
        <span className="min-w-0 flex-1">
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span className="text-[14px] font-medium text-ink-900">{a.label}</span>
            {a.kinds.map((k) => (
              <Badge key={k} tone="neutral">
                {KIND_LABEL[k]}
              </Badge>
            ))}
            {a.currentVersion && <Badge tone="ink">v{a.currentVersion}</Badge>}
            {runtimeLabel(runtimeOf(a)) && <Badge tone="neutral">{runtimeLabel(runtimeOf(a))}</Badge>}
          </span>
          <code className="block truncate font-mono text-[11.5px] text-ink-400">{a.id}</code>
          {(a.summary || a.publisher) && (
            <span className="mt-0.5 block text-[12.5px] text-ink-600">
              {a.summary}
              {a.publisher && <span className="text-ink-400">{a.summary ? " · " : ""}by {a.publisher}</span>}
            </span>
          )}
        </span>
        <span className="shrink-0 text-right text-[12px] text-ink-500">
          <span className="block">{used === 0 ? "Not used" : `Used by ${used} subscription${used === 1 ? "" : "s"}`}</span>
          {!a.native && (
            <span className="block text-ink-400">
              {pinnable === 0 ? "No versions" : `${pinnable} version${pinnable === 1 ? "" : "s"}`}
            </span>
          )}
        </span>
      </button>

      {open && <AdapterDetails adapter={a} usage={usage} />}
    </li>
  );
}

function AdapterIcon({ adapter: a }: { adapter: InventoryAdapter }) {
  if (a.icon) return <img src={a.icon} alt="" className="size-9 shrink-0 rounded-lg object-contain" />;
  return (
    <span
      aria-hidden
      className={`flex size-9 shrink-0 items-center justify-center rounded-lg ${
        a.native ? "bg-ink-100 text-ink-500" : "bg-crimson-50 text-crimson-600"
      }`}
    >
      <Puzzle className="size-4.5" />
    </span>
  );
}

function AdapterDetails({ adapter: a, usage }: { adapter: InventoryAdapter; usage?: AdapterUsage }) {
  return (
    <div className="space-y-5 border-t border-ink-100 bg-ink-50/40 px-4 py-4 pl-[4.25rem]">
      {a.description && <p className="max-w-3xl text-[13px] whitespace-pre-line text-ink-700">{a.description}</p>}
      {a.tags.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {a.tags.map((t) => (
            <span key={t} className="rounded-full bg-white px-2 py-0.5 text-[11.5px] text-ink-600 ring-1 ring-ink-200">
              {t}
            </span>
          ))}
        </div>
      )}

      {!a.native && <VersionHistory adapter={a} usage={usage} />}

      <div>
        <h3 className="mb-1.5 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">Settings</h3>
        {a.props.length === 0 ? (
          <p className="text-[13px] text-ink-500">None.</p>
        ) : (
          <table className="w-full max-w-3xl text-left text-[12.5px]">
            <thead className="text-ink-500">
              <tr>
                <th className="py-1 pr-3 font-medium">Name</th>
                <th className="py-1 pr-3 font-medium">Required</th>
                <th className="py-1 pr-3 font-medium">Default</th>
                <th className="py-1 font-medium">Description</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100">
              {a.props.map((p) => (
                <tr key={p.key}>
                  <td className="py-1.5 pr-3 font-mono text-ink-800">
                    {p.key}
                    {p.secret && (
                      <Badge tone="warn" className="ml-1.5">
                        Secret
                      </Badge>
                    )}
                  </td>
                  <td className="py-1.5 pr-3 text-ink-600">{p.optional ? "No" : "Yes"}</td>
                  <td className="py-1.5 pr-3 font-mono text-ink-600">{p.default ?? "—"}</td>
                  <td className="py-1.5 text-ink-600">{p.description ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {usage && usage.subscriptions.length > 0 && (
        <div>
          <h3 className="mb-1.5 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">Used by</h3>
          <ul className="flex flex-wrap gap-x-4 gap-y-1 text-[13px]">
            {usage.subscriptions.map((s) => (
              <li key={s.id}>
                <Link to={`/subscriptions/${s.id}`} className="font-medium text-crimson-700 hover:underline">
                  {s.name}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

/**
 * Every published version, newest first: which is current, which are withdrawn, what changed, and
 * how many subscriptions are pinned to each. An adapter published without a manifest has only its
 * version files, so it shows just their numbers.
 */
function VersionHistory({ adapter: a, usage }: { adapter: InventoryAdapter; usage?: AdapterUsage }) {
  const canReadSource = useSessionCan("adapter-source.view");
  const canEdit = useSessionCan("adapter-source.edit");
  const canPromote = useSessionCan("adapter-source.operate");
  const [sourceOf, setSourceOf] = useState<string | null>(null);
  const [promoting, setPromoting] = useState<string | null>(null);
  const [withdrawing, setWithdrawing] = useState<string | null>(null);
  const [editError, setEditError] = useState("");
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const withSource = a.versions.filter((v) => v.hasSource).map((v) => v.version);
  const sourceColumn = canReadSource && withSource.length > 0;
  const editable = (v: (typeof a.versions)[number]) =>
    canEdit && v.hasSource && (v.runtime === "python" || v.runtime === "node");
  const promotable = (v: (typeof a.versions)[number]) => canPromote && !v.withdrawn && v.version !== a.currentVersion;
  // Not the current one: the server refuses, since everything following current would have nothing to run.
  const withdrawable = promotable;
  const actionColumn = a.hasCatalog && a.versions.some((v) => editable(v) || promotable(v));
  const edit = async (version: string) => {
    setEditError("");
    try {
      const id = await api.draftFromVersion(a.id, version);
      await queryClient.invalidateQueries({ queryKey: keys.adapterDrafts });
      navigate(`/adapters/drafts/${id}`);
    } catch (e) {
      setEditError(e instanceof Error ? e.message : "A draft couldn't be started from it.");
    }
  };

  return (
    <div>
      <h3 className="mb-1.5 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">Versions</h3>
      {a.versions.length === 0 ? (
        <p className="text-[13px] text-ink-500">
          Published without versions: every subscription runs the package that was uploaded last.
        </p>
      ) : (
        <>
          <table className="w-full max-w-3xl text-left text-[12.5px]">
            <thead className="text-ink-500">
              <tr>
                <th className="py-1 pr-3 font-medium">Version</th>
                <th className="py-1 pr-3 font-medium">Runtime</th>
                <th className="py-1 pr-3 font-medium">Published</th>
                <th className="py-1 pr-3 font-medium">Pinned by</th>
                <th className="py-1 font-medium">Release notes</th>
                {sourceColumn && <th className="py-1 pl-3 font-medium">Source</th>}
                {actionColumn && <th className="py-1 pl-3 font-medium"><span className="sr-only">Actions</span></th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100">
              {a.versions.map((v) => {
                const pinned = usage?.pinned[v.version] ?? 0;
                return (
                  <tr key={v.version} className={v.withdrawn ? "text-ink-400" : "text-ink-700"}>
                    <td className="py-1.5 pr-3 whitespace-nowrap">
                      <span className="font-mono">v{v.version}</span>
                      {v.version === a.currentVersion && (
                        <Badge tone="ok" className="ml-1.5">
                          Current
                        </Badge>
                      )}
                      {v.withdrawn && (
                        <Badge tone="neutral" className="ml-1.5">
                          Withdrawn
                        </Badge>
                      )}
                    </td>
                    <td className="py-1.5 pr-3 whitespace-nowrap">{runtimeLabel(v.runtime) ?? "—"}</td>
                    <td className="py-1.5 pr-3 whitespace-nowrap">
                      {v.publishedOn ? formatDate(v.publishedOn) : "—"}
                      {v.publishedBy && <span className="block text-[11.5px] text-ink-400">{v.publishedBy}</span>}
                    </td>
                    <td className="py-1.5 pr-3">{pinned === 0 ? "—" : pinned}</td>
                    <td className="py-1.5 whitespace-pre-line">{v.releaseNotes ?? ""}</td>
                    {sourceColumn && (
                      <td className="py-1.5 pl-3 whitespace-nowrap">
                        {v.hasSource ? (
                          <button
                            type="button"
                            onClick={() => setSourceOf(v.version)}
                            aria-label={`View source of v${v.version}`}
                            className="inline-flex items-center gap-1 font-medium text-crimson-700 hover:underline"
                          >
                            <FileCode className="size-3.5" aria-hidden />
                            View
                          </button>
                        ) : (
                          <span className="text-ink-400" title="Published without its source">
                            —
                          </span>
                        )}
                      </td>
                    )}
                    {actionColumn && (
                      <td className="py-1.5 pl-3 whitespace-nowrap">
                        <span className="flex gap-3">
                          {editable(v) && (
                            <button
                              type="button"
                              onClick={() => void edit(v.version)}
                              aria-label={`Edit v${v.version}`}
                              className="inline-flex items-center gap-1 font-medium text-crimson-700 hover:underline"
                            >
                              <Pencil className="size-3.5" aria-hidden />
                              Edit
                            </button>
                          )}
                          {promotable(v) && (
                            <button
                              type="button"
                              onClick={() => setPromoting(v.version)}
                              aria-label={`Make v${v.version} current`}
                              className="font-medium text-crimson-700 hover:underline"
                            >
                              Make current
                            </button>
                          )}
                          {withdrawable(v) && (
                            <button
                              type="button"
                              onClick={() => setWithdrawing(v.version)}
                              aria-label={`Withdraw v${v.version}`}
                              className="font-medium text-ink-600 hover:text-danger-700 hover:underline"
                            >
                              Withdraw
                            </button>
                          )}
                        </span>
                      </td>
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
          {usage && usage.followingCurrent > 0 && (
            <p className="mt-1.5 text-[12px] text-ink-500">
              {usage.followingCurrent === 1
                ? "1 use follows the current version."
                : `${usage.followingCurrent} uses follow the current version.`}
            </p>
          )}
          <FormError>{editError}</FormError>
          {promoting && (
            <ConfirmDialog
              title={`Make v${promoting} current?`}
              body={`Every subscription using ${a.label} that doesn't pin a version will run v${promoting} from its next exchange.${
                a.currentVersion ? ` To go back, make v${a.currentVersion} current again.` : ""
              }`}
              confirmLabel={`Make v${promoting} current`}
              confirmVariant="primary"
              onConfirm={async () => {
                await api.promoteAdapter(a.id, promoting);
                await queryClient.invalidateQueries({ queryKey: ["adapters"] });
              }}
              onClose={() => setPromoting(null)}
            />
          )}
          {withdrawing && (
            <ConfirmDialog
              title={`Withdraw v${withdrawing}?`}
              body={`It stays listed, but can't be pinned or made current again.${
                (usage?.pinned[withdrawing] ?? 0) > 0
                  ? ` The ${usage!.pinned[withdrawing] === 1 ? "subscription" : `${usage!.pinned[withdrawing]} subscriptions`} pinned to it keep running it until they are moved.`
                  : ""
              } Nothing is deleted.`}
              confirmLabel={`Withdraw v${withdrawing}`}
              onConfirm={async () => {
                await api.withdrawAdapterVersion(a.id, withdrawing);
                await queryClient.invalidateQueries({ queryKey: ["adapters"] });
              }}
              onClose={() => setWithdrawing(null)}
            />
          )}
          {sourceColumn && sourceOf && (
            <div className="mt-3">
              <Suspense fallback={<LoadingBlock label="Opening the source…" />}>
                <AdapterSourceViewer
                  key={sourceOf}
                  adapterId={a.id}
                  label={a.label}
                  versions={withSource}
                  initialVersion={sourceOf}
                  onClose={() => setSourceOf(null)}
                />
              </Suspense>
            </div>
          )}
          {!a.hasCatalog && (
            <p className="mt-1.5 text-[12px] text-ink-500">
              Published by an older installer: version numbers only, with no notes or publish dates.
            </p>
          )}
        </>
      )}
    </div>
  );
}

const LANGUAGES = [
  { value: "python", label: "Python" },
  { value: "typescript", label: "TypeScript" },
  { value: "node", label: "JavaScript" },
];

/** Starts a new Python or JavaScript adapter in the editor, from the template serverless init writes. */
/**
 * Publishes a package built elsewhere, by the bitween CLI, in any language .NET included: what
 * bitween adapter publish does, from the browser.
 */
function UploadPackageButton() {
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [version, setVersion] = useState("");
  const [current, setCurrent] = useState(false);
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [done, setDone] = useState<AdapterUploadResult | null>(null);
  const queryClient = useQueryClient();

  const close = () => {
    setOpen(false);
    setFile(null);
    setVersion("");
    setCurrent(false);
    setNotes("");
    setError("");
    setDone(null);
  };

  const upload = async () => {
    if (!file) return;
    setBusy(true);
    setError("");
    try {
      setDone(await api.uploadAdapterPackage(file, { version, current, releaseNotes: notes }));
      await queryClient.invalidateQueries({ queryKey: ["adapters"] });
    } catch (e) {
      setError(e instanceof Error ? e.message : "The package couldn't be published.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Button onClick={() => setOpen(true)}>
        <Upload className="size-4" aria-hidden />
        Upload package
      </Button>
      {open && (
        <Dialog title="Upload an adapter package" onClose={close}>
          {done ? (
            <div className="space-y-4">
              <p className="text-sm text-ink-700">
                Published <code className="font-mono">{done.adapterId}</code> v{done.version}
                {done.current ? ", and made it current." : ". It isn't current: make it current on its row when it should run."}
              </p>
              <div className="flex justify-end">
                <Button variant="primary" onClick={close}>
                  Done
                </Button>
              </div>
            </div>
          ) : (
            <form
              className="space-y-4"
              onSubmit={(e) => {
                e.preventDefault();
                void upload();
              }}
            >
              <Field
                label="Package"
                hint="The .zip bitween adapter build made. Its manifest says which adapter it is; nothing in it runs while it is published."
                htmlFor="upload-package"
              >
                <input
                  id="upload-package"
                  type="file"
                  accept=".zip,application/zip"
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                  className="block w-full text-sm text-ink-700 file:mr-3 file:rounded-md file:border file:border-ink-200 file:bg-white file:px-3 file:py-1.5 file:text-sm"
                />
              </Field>
              <Field label="Version" htmlFor="upload-version">
                <Select
                  id="upload-version"
                  value={version}
                  onChange={(e) => setVersion(e.target.value)}
                  options={[
                    { value: "", label: "The package's own" },
                    { value: "patch", label: "Next patch" },
                    { value: "minor", label: "Next minor" },
                    { value: "major", label: "Next major" },
                  ]}
                />
              </Field>
              <Field label="Release notes" hint="Replaces the package's own, if it has any." htmlFor="upload-notes">
                <TextInput id="upload-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
              </Field>
              <label className="flex items-center gap-2 text-sm text-ink-700">
                <input type="checkbox" checked={current} onChange={(e) => setCurrent(e.target.checked)} />
                Make it current: subscriptions that don't pin a version run it from their next exchange
              </label>
              <FormError>{error}</FormError>
              <div className="flex justify-end gap-2">
                <Button onClick={close}>Cancel</Button>
                <Button variant="primary" type="submit" busy={busy} disabled={!file}>
                  Publish
                </Button>
              </div>
            </form>
          )}
        </Dialog>
      )}
    </>
  );
}

function NewAdapterButton() {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [language, setLanguage] = useState("python");
  const [kind, setKind] = useState<AdapterKind>("handler");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const create = async () => {
    setBusy(true);
    setError("");
    try {
      const id = await api.createAdapterDraft({ name: name.trim(), language: language as "python", kind });
      await queryClient.invalidateQueries({ queryKey: keys.adapterDrafts });
      navigate(`/adapters/drafts/${id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "The adapter couldn't be started.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Button variant="primary" onClick={() => setOpen(true)}>
        <Plus className="size-4" aria-hidden />
        New adapter
      </Button>
      {open && (
        <Dialog title="New adapter" onClose={() => setOpen(false)}>
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              void create();
            }}
          >
            <Field label="Name" hint="Letters and digits, like AcmeOrders. Its id comes from it: acme.orders." htmlFor="new-adapter-name">
              <TextInput id="new-adapter-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
            </Field>
            <Field label="Language" htmlFor="new-adapter-language">
              <Select id="new-adapter-language" value={language} onChange={(e) => setLanguage(e.target.value)} options={LANGUAGES} />
            </Field>
            <Field label="Kind" htmlFor="new-adapter-kind">
              <Select
                id="new-adapter-kind"
                value={kind}
                onChange={(e) => setKind(e.target.value as AdapterKind)}
                options={ADAPTER_KINDS.map((k) => ({ value: k, label: KIND_LABEL[k] }))}
              />
            </Field>
            <p className="text-[12.5px] text-ink-500">
              It starts as a draft only you and other editors see, and runs nowhere until a version is published and made
              current. Adapters that need packages beyond the SDK are built with the bitween CLI, unless this Bitween's
              editor fetches dependencies (see Settings › About this instance).
            </p>
            <FormError>{error}</FormError>
            <div className="flex justify-end gap-2">
              <Button onClick={() => setOpen(false)}>Cancel</Button>
              <Button variant="primary" type="submit" busy={busy} disabled={!name.trim()}>
                Start writing
              </Button>
            </div>
          </form>
        </Dialog>
      )}
    </>
  );
}

const DRAFT_LANGUAGE = { python: "Python", node: "JavaScript", typescript: "TypeScript" } as const;

/** Adapters being written in the editor, so they can be picked up again. */
function Drafts() {
  const drafts = useQuery({ queryKey: keys.adapterDrafts, queryFn: () => api.listAdapterDrafts() });
  if (!drafts.data || drafts.data.length === 0) return null;
  return (
    <section aria-labelledby="section-drafts">
      <h2 id="section-drafts" className="mb-2.5 flex items-center gap-2 text-[15px] font-semibold text-ink-900">
        Drafts
        <span className="rounded-full bg-ink-100 px-2 py-0.5 text-[11.5px] font-medium text-ink-600">{drafts.data.length}</span>
      </h2>
      <ul className="divide-y divide-ink-100 overflow-hidden rounded-xl border border-ink-200 bg-white">
        {drafts.data.map((d) => (
          <li key={d.id}>
            <Link to={`/adapters/drafts/${d.id}`} className="flex items-center gap-3 px-4 py-2.5 hover:bg-ink-50/60">
              <Pencil className="size-4 text-ink-400" aria-hidden />
              <code className="font-mono text-[13px] text-ink-900">{d.adapterId}</code>
              <Badge tone="neutral">{DRAFT_LANGUAGE[d.language]}</Badge>
              <span className="text-[12.5px] text-ink-500">{d.baseVersion ? `from v${d.baseVersion}` : "not yet published"}</span>
              <span className="ml-auto text-[12px] text-ink-400">
                {formatDate(d.modifiedOn ?? d.createdOn)}
                {(d.modifiedBy ?? d.createdBy) && ` · ${d.modifiedBy ?? d.createdBy}`}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
