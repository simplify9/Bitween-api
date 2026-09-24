import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, CircleHelp, Gauge, Plug, Plus, Telescope, Trash2, X } from "lucide-react";
import {
  api,
  ApiRequestError,
  SECRET_SENTINEL,
  type DataSourceInspectResult,
  type DataSourceTestResult,
} from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, PasswordInput, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { BackLink } from "../../components/ui/BackLink";
import { UnsavedBar } from "../../components/ui/Panel";
import { keys } from "../../api/queryKeys";
import { ConnectionBadge } from "./ConnectionBadge";
import {
  isSecretName,
  providerOf,
  settingLabel,
  settingOf,
  useDataSourceProviders,
  orderedSettingNames,
} from "./providers";
import { LiveConnection } from "./LiveConnection";
import { Statements, type StatementSeed } from "./Statements";
import { SchemaBrowser } from "./SchemaBrowser";
import { draftOf, editableFingerprint, type Draft } from "./draft";

/** A draft is the whole editable surface, so the save bar can compare against what was loaded. */

/**
 * One connection: its settings, whether it works, and who is holding it.
 *
 * The Test button carries more weight than it looks. Without it the only way to discover a wrong
 * password is to save, wait up to thirty seconds for the supervisor to reconcile, and then read the
 * health row — so a typo costs a minute and a guess at which of six fields caused it.
 */
export function DataSourcePage() {
  const { id = "" } = useParams();
  const dataSourceId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("data-sources.edit");

  const providers = useDataSourceProviders();
  const source = useQuery({
    queryKey: keys.dataSources.detail(dataSourceId),
    queryFn: () => api.getDataSource(dataSourceId),
    retry: false,
    refetchInterval: 10_000,
  });

  const [draft, setDraft] = useState<Draft | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<DataSourceTestResult | null>(null);

  // The statement steps, separated from the connection steps: a failure among them means the
  // connection is fine and some SQL is not, which is a different thing to go and fix.
  const statementStages = (result?.stages ?? []).filter((s) => s.name.startsWith("statement:"));
  const failedStatements = statementStages.filter((s) => !s.succeeded);
  const [newKey, setNewKey] = useState("");
  const [inspect, setInspect] = useState<DataSourceInspectResult | null>(null);

  // A statement the schema browser wrote, handed up here because the panel that shows it sits
  // above the browser that produced it.
  const [seed, setSeed] = useState<StatementSeed | null>(null);
  const statementsRef = useRef<HTMLDivElement>(null);
  const canCreateStatements = useSessionCan("data-source-statements.create");
  const [removing, setRemoving] = useState(false);

  // Re-seed when the server's copy of the SETTINGS changes — saving re-masks the secrets, so the
  // form has to go back to showing "stored" rather than a value the server will never return
  // again. Not on every response: this query is polled for the connection panel, and re-seeding on
  // each poll silently threw away anything typed but not yet saved.
  const seeded = useRef<string | null>(null);
  useEffect(() => {
    if (!source.data) return;

    const fingerprint = editableFingerprint(source.data);
    if (seeded.current === fingerprint) return;

    seeded.current = fingerprint;
    setDraft(draftOf(source.data));
  }, [source.data]);

  const save = useMutation({
    mutationFn: (d: Draft) =>
      api.updateDataSource(dataSourceId, {
        name: d.name,
        adapterId: source.data!.adapterId,
        kind: source.data!.kind,
        properties: d.properties,
        secretProperties: source.data!.secretProperties,
        inactive: d.inactive,
        deduplicationWindowDays: d.deduplicationWindowDays,
        softMemoryLimitMb: d.softMemoryLimitMb,
        hardMemoryLimitMb: d.hardMemoryLimitMb,
        cpuPercentLimit: d.cpuPercentLimit,
        cpuLimitSamples: d.cpuLimitSamples,
      }),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: keys.dataSources.all });
    },
    onError: (e) => setError(e instanceof ApiRequestError ? e.message : "Could not save."),
  });

  const test = useMutation({
    mutationFn: () => api.testDataSource(dataSourceId),
    onSuccess: setResult,
    onError: (e) =>
      setResult({
        succeeded: false,
        error: e instanceof ApiRequestError ? e.message : "The test could not be run.",
        stages: [],
      }),
  });

  // Discover and GetStats go to the adapter actually serving traffic, not to a throwaway instance
  // the way Test does — they are questions about the live connection.
  const ask = useMutation({
    mutationFn: (command: string) => api.inspectDataSource(dataSourceId, command),
    onSuccess: setInspect,
    onError: (e) =>
      setInspect({
        ran: false,
        command: null,
        result: null,
        error: e instanceof ApiRequestError ? e.message : "The command could not be run.",
      }),
  });

  const remove = useMutation({
    mutationFn: () => api.deleteDataSource(dataSourceId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: keys.dataSources.all });
      navigate("/data-sources");
    },
    onError: (e) => {
      setRemoving(false);
      setError(e instanceof ApiRequestError ? e.message : "Could not delete this data source.");
    },
  });

  if (source.isPending) return <LoadingBlock label="Loading data source…" />;
  if (source.isError || !source.data || !draft)
    return <FormError>This data source no longer exists.</FormError>;

  const d = source.data;

  // Only ever used to illustrate what a percentage means. This is the BROWSER's core count, not
  // the node's, so it is a rough translation rather than a claim about the server.
  const cores = navigator.hardwareConcurrency || 8;
  const provider = providerOf(providers.data, d.adapterId);
  const dirty = JSON.stringify(draft) !== JSON.stringify(draftOf(d));
  // A database has no queue to redeliver from and no bus gateway to feed, so the broker-only
  // parts of this page are left off it rather than shown empty.
  const relational = d.kind === "Relational";

  const setProperty = (key: string, value: string) =>
    setDraft({ ...draft, properties: { ...draft.properties, [key]: value } });

  const removeProperty = (key: string) => {
    const properties = { ...draft.properties };
    delete properties[key];
    setDraft({ ...draft, properties });
  };

  // Declared by the adapter but not on this data source yet — offered rather than imposed, so a
  // form does not open with twenty empty boxes.
  const unused = (provider?.settings ?? [])
    .map((setting) => setting.name)
    .filter((name) => !(name in draft.properties));

  const addProperty = () => {
    const key = newKey.trim();
    if (!key || key in draft.properties) return;
    setProperty(key, "");
    setNewKey("");
  };

  return (
    <div className={canEdit && dirty ? "pb-24" : undefined}>
      {removing && (
        <ConfirmDialog
          title={`Delete "${d.name}"?`}
          confirmLabel="Delete"
          onClose={() => setRemoving(false)}
          onConfirm={() => remove.mutateAsync()}
          body={
            d.gatewayCount > 0
              ? `${d.gatewayCount} bus gateway(s) still read from this connection, so deleting will be refused until they are moved off it.`
              : d.subscriptionCount > 0
                ? `${d.subscriptionCount} subscription(s) still run on this connection, so deleting will be refused until they are moved off it.`
                : relational
                  ? "Every node closes its connection to this database."
                  : "Its remembered deduplication keys go with it, so a message already processed could be handled again if it arrives later."
          }
        />
      )}

      <BackLink to="/data-sources" label="Data sources" className="mb-3" />

      <PageHeader
        title={d.name}
        description={provider?.description ?? d.adapterId}
        actions={
          <div className="flex items-center gap-2">
            <Can permission="data-sources.operate">
              <Button onClick={() => test.mutate()} disabled={test.isPending}>
                <Plug className="size-4" /> {test.isPending ? "Testing…" : "Test connection"}
              </Button>
            </Can>
            {/* A relational source has the schema browser below, which is this answer made
                usable — two ways to ask the same question, one of them 1,800 lines of JSON, is
                one too many. A broker has no browser, so Discover is still how its topology is
                seen. */}
            {source.data?.kind !== "Relational" && (
              <Button onClick={() => ask.mutate("Discover")} disabled={ask.isPending}>
                <Telescope className="size-4" /> Discover
              </Button>
            )}
            <Button onClick={() => ask.mutate("GetStats")} disabled={ask.isPending}>
              <Gauge className="size-4" /> Stats
            </Button>
            <Can permission="data-sources.delete">
              <Button onClick={() => setRemoving(true)}>
                <Trash2 className="size-4" /> Delete
              </Button>
            </Can>
          </div>
        }
      />

      {/* ——— the test's answer ——— */}
      {result && (
        <section
          className={`mb-4 rounded-xl border p-4 ${
            result.succeeded ? "border-ok-200 bg-ok-50" : "border-danger-200 bg-danger-50"
          }`}
        >
          <div className="mb-2 flex items-center gap-2">
            <h2 className="text-sm font-semibold text-ink-900">
              {result.succeeded ? "The connection works" : "The connection failed"}
            </h2>
            <Badge tone={result.succeeded ? "ok" : "danger"}>{result.succeeded ? "OK" : "Failed"}</Badge>
            {/* Which statements, out of how many — the number that says whether this is one typo
                or the same mistake copied across a set. */}
            {failedStatements.length > 0 && (
              <Badge tone="danger">
                {failedStatements.length} of {statementStages.length} statements
              </Badge>
            )}
          </div>

          <ul className="flex flex-col gap-1">
            {result.stages.map((stage, i) => (
              <li
                key={i}
                className={`flex items-start gap-2 text-sm ${
                  stage.succeeded ? "" : "rounded-md bg-danger-100/60 px-1.5 py-1"
                }`}
              >
                {stage.succeeded ? (
                  <Check className="mt-0.5 size-4 shrink-0 text-ok-700" />
                ) : (
                  <X className="mt-0.5 size-4 shrink-0 text-danger-700" />
                )}
                <span
                  className={`w-56 shrink-0 font-medium ${
                    stage.succeeded ? "text-ink-800" : "text-danger-800"
                  }`}
                >
                  {stage.name}
                </span>
                {/* pre-wrap because a driver's message carries its own line breaks — PostgreSQL
                    puts the character offset on a line of its own, and collapsed into a paragraph
                    it reads as part of the sentence before it. min-w-0 so a long one wraps inside
                    the row instead of widening the card. */}
                <span
                  className={`min-w-0 flex-1 whitespace-pre-wrap ${
                    stage.succeeded ? "text-ink-600" : "text-danger-800"
                  }`}
                >
                  {stage.detail}
                </span>
              </li>
            ))}
          </ul>

          {/* Only when it is not already a stage's own detail: a failure inside a statement is
              reported against that statement, and repeating it underneath reads as a second,
              different problem. */}
          {!result.succeeded && result.error && failedStatements.length === 0 && (
            <p className="mt-2 whitespace-pre-wrap text-sm text-danger-800">{result.error}</p>
          )}

          {failedStatements.length > 0 && (
            <p className="mt-2 text-[12px] text-danger-800">
              Statements are prepared against the live schema, never run. Fix them on this page —
              a statement is refused when it is saved now, so these predate that check or the
              schema moved underneath them.
            </p>
          )}

          <p className="mt-2 text-[12px] text-ink-500">
            {source.data?.kind === "Relational"
              ? "The test runs the real adapter against these settings on its own throwaway connection, "
                + "and prepares every statement against the live schema — so a typo or a dropped column "
                + "fails here rather than on the first message."
              : "The test runs the real adapter against these settings but never consumes: the queues "
                + "its gateways read are inspected, not drained."}
          </p>
        </section>
      )}

      {/* Settings on the left, everything about the running connection on the right. The status
          column comes first in the markup so a narrow screen still opens on whether it works. */}
      <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-2">
        <div className="min-w-0">
          {/* ——— what the last heartbeat said, and what it is doing right now ——— */}
          <section className="mb-4 rounded-xl border border-ink-200 bg-white p-4">
            <div className="mb-3 flex items-center gap-3">
              <h2 className="text-sm font-semibold text-ink-900">Connection</h2>
              <ConnectionBadge state={d.lastKnownState} failures={d.consecutiveFailures} />
            </div>

            <dl className="grid grid-cols-1 gap-x-8 gap-y-2 text-sm sm:grid-cols-2">
              <Row label="Held by">{d.ownedByNode ?? "Not running on any node"}</Row>
              <Row label="Last heartbeat">
                {d.lastHeartbeatOn ? new Date(d.lastHeartbeatOn).toLocaleString() : "—"}
              </Row>
              <Row label="Consecutive restarts">{String(d.consecutiveFailures)}</Row>
              {relational ? (
                <Row label="Subscriptions using this">{String(d.subscriptionCount)}</Row>
              ) : (
                <Row label="Bus gateways reading this">
                  <Link to="/bus-gateways" className="text-crimson-700 hover:underline">
                    {d.gatewayCount}
                  </Link>
                </Row>
              )}
              {d.lastException && (
                <div className="sm:col-span-2">
                  <dt className="text-ink-500">Last error</dt>
                  <dd className="text-danger-700">{d.lastException}</dd>
                </div>
              )}
            </dl>

            {/* The two placements are opposites, and the wrong explanation directly contradicts the
                "Held by" value right above it — which is how someone concludes the page is broken. */}
            <p className="mt-3 text-[12px] text-ink-500">
              {source.data?.kind === "Relational" ? (
                <>
                  A connection pool is held by every node that runs work, not leased to one: a node
                  without it could not run the exchanges that need it. Nothing here is exclusive, so
                  there is no fencing token and no ownership to move.
                </>
              ) : (
                <>
                  A broker connection is exclusive, so exactly one node holds it. The term after the
                  node name is the fencing token — it increases every time ownership moves, and a node
                  whose term is no longer current stops immediately rather than carrying on consuming.
                </>
              )}
            </p>

            <LiveConnection dataSourceId={dataSourceId} knownError={d.lastException} />
          </section>

          {/* Only a relational source runs SQL, and the server refuses a statement on anything else —
              so offering the panel on a broker would be offering a thing that cannot work. */}
          {source.data?.kind === "Relational" && (
            <>
              <div ref={statementsRef}>
                <Can permission="data-source-statements.view">
                  <Statements
                    dataSourceId={dataSourceId}
                    seed={seed}
                    onSeedConsumed={() => setSeed(null)}
                  />
                </Can>
              </div>

              {/* Below the statements it feeds, because that is the direction the work runs: find the
                  table, then write the statement. The browser is gated on data-sources.view like the
                  rest of this page; handing a draft to the form is gated separately, since writing
                  SQL is a different job from reading a catalog. */}
              <div className="mb-4">
                <SchemaBrowser
                  dataSourceId={dataSourceId}
                  onUseInStatement={
                    canCreateStatements
                      ? (draft) => {
                          setSeed(draft);
                          // The form is above the browser, and a draft appearing off-screen reads as
                          // a button that did nothing.
                          statementsRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
                        }
                      : undefined
                  }
                />
              </div>
            </>
          )}

          {/* ——— what the live adapter says it can see ——— */}
          {inspect && (
            <section className="mb-4 rounded-xl border border-ink-200 bg-white p-4">
              <div className="mb-2 flex items-center gap-2">
                <h2 className="text-sm font-semibold text-ink-900">
                  {inspect.command === "GetStats"
                    ? "Adapter statistics"
                    : source.data?.kind === "Relational"
                      ? "What is in the database"
                      : "What is on the broker"}
                </h2>
                <button
                  type="button"
                  onClick={() => setInspect(null)}
                  className="ml-auto text-[12px] text-ink-500 hover:text-ink-800"
                >
                  dismiss
                </button>
              </div>

              {inspect.ran ? (
                /* Capped and scrolled: a database's catalog runs to every table, view and routine the
                   role can see, and printed in full it buries the settings form under a page of JSON
                   with no way back but the scrollbar. */
                <pre className="max-h-96 overflow-auto rounded-lg bg-ink-50 p-3 font-mono text-[12px] text-ink-800">
                  {inspect.result}
                </pre>
              ) : (
                <p className="text-sm text-ink-600">{inspect.error}</p>
              )}

              <p className="mt-2 text-[12px] text-ink-500">
                Asked of the connection that is actually serving traffic, not a throwaway one — and
                read-only:{" "}
                {source.data?.kind === "Relational"
                  ? "it reads the catalog, and writes nothing."
                  : "nothing is consumed, acknowledged or published."}
              </p>
            </section>
          )}
        </div>

        {/* ——— settings ——— */}
        <section className="min-w-0 rounded-xl border border-ink-200 bg-white p-5 lg:order-first">
          <h2 className="mb-4 text-sm font-semibold text-ink-900">Settings</h2>

          <div className="flex flex-col gap-4">
            <Field label="Name" htmlFor="ds-name">
              <TextInput
                id="ds-name"
                value={draft.name}
                disabled={!canEdit}
                onChange={(e) => setDraft({ ...draft, name: e.target.value })}
              />
            </Field>

            <Checkbox
              checked={!draft.inactive}
              disabled={!canEdit}
              onChange={(e) => setDraft({ ...draft, inactive: !e.target.checked })}
              label="Active"
              description="Turning this off stops the connection without losing its settings."
            />

            {!relational && (
              <Field
                label="Remember deduplication keys for (days)"
                htmlFor="ds-dedupe"
                hint="Has to exceed the widest redelivery window this broker can produce — its message TTL, a dead-letter replay, someone re-driving a queue by hand. A key forgotten too early lets a redelivery through as a fresh message. Zero turns deduplication off."
              >
                <TextInput
                  id="ds-dedupe"
                  type="number"
                  min={0}
                  value={draft.deduplicationWindowDays}
                  disabled={!canEdit}
                  onChange={(e) =>
                    setDraft({ ...draft, deduplicationWindowDays: Number(e.target.value) || 0 })
                  }
                />
              </Field>
            )}

            {/* All four in one row. Each explanation sits on its label's help mark: as paragraphs
                they were most of this card. */}
            <div className="border-t border-ink-100 pt-4">
              <h3 className="mb-1 text-sm font-medium text-ink-800">Resource ceilings</h3>
              <p className="mb-3 text-[12px] text-ink-500">
                0 uses the host default. Changing these restarts the adapter; nothing in flight is
                lost, because messages are only acknowledged once Bitween has persisted them.
              </p>

              <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                <CeilingInput
                  id="ds-soft"
                  label="Soft memory (MB)"
                  help="An adapter is a separate process, so without a ceiling one runaway payload can take the whole node down. Crossing the soft limit recycles the adapter between messages, so nothing in flight is lost."
                  value={draft.softMemoryLimitMb}
                  disabled={!canEdit}
                  onChange={(v) => setDraft({ ...draft, softMemoryLimitMb: v })}
                />
                <CeilingInput
                  id="ds-hard"
                  label="Hard memory (MB)"
                  help="The runtime's own ceiling: an allocation past it fails inside the adapter."
                  value={draft.hardMemoryLimitMb}
                  disabled={!canEdit}
                  onChange={(v) => setDraft({ ...draft, hardMemoryLimitMb: v })}
                />
                <CeilingInput
                  id="ds-cpu"
                  label="CPU (% of node)"
                  help="A share of the whole node, not of one core: one core flat out on a sixteen-core node reads about 6%. Crossing it asks the adapter to drain rather than killing it, so in-flight messages go back instead of being lost."
                  value={draft.cpuPercentLimit}
                  max={100}
                  step={0.5}
                  disabled={!canEdit}
                  onChange={(v) => setDraft({ ...draft, cpuPercentLimit: v })}
                />
                <CeilingInput
                  id="ds-cpu-samples"
                  label="CPU heartbeats"
                  help="How many heartbeats in a row it must stay above the CPU line before it trips. Higher lets a bigger burst of real work pass — an adapter draining a backlog is supposed to work hard."
                  value={draft.cpuLimitSamples}
                  disabled={!canEdit}
                  onChange={(v) => setDraft({ ...draft, cpuLimitSamples: v })}
                />
              </div>

              {draft.softMemoryLimitMb > 0 &&
                draft.hardMemoryLimitMb > 0 &&
                draft.softMemoryLimitMb > draft.hardMemoryLimitMb && (
                  <p className="mt-2 text-[12px] text-danger-700">
                    A soft limit above the hard one can never be reached — the runtime fails the
                    allocation first, so the recycle never happens.
                  </p>
                )}

              {draft.cpuPercentLimit > 100 ? (
                <p className="mt-2 text-[12px] text-danger-700">
                  Above 100% can never be reached — the figure is a share of the whole node, so 100%
                  is every core at once.
                </p>
              ) : (
                draft.cpuPercentLimit > 0 && (
                  <p className="mt-2 text-[12px] text-ink-500">
                    For scale: {((draft.cpuPercentLimit / 100) * cores).toFixed(1)} core(s) on a{" "}
                    {cores}-core machine — this browser's core count, not the node's.
                  </p>
                )
              )}
            </div>

            <div className="border-t border-ink-100 pt-4">
              <h3 className="mb-1 text-sm font-medium text-ink-800">Connection settings</h3>
              <p className="mb-3 text-[12px] text-ink-500">
                Handed straight to the adapter, which is also where this list comes from:{" "}
                {provider ? provider.label : d.adapterId} declares what it accepts. Anything else it
                understands can still be added by hand.
              </p>

              <div className="flex flex-col gap-3">
                {orderedSettingNames(provider, Object.keys(draft.properties)).map((key) => {
                  const declared = settingOf(provider, key);
                  const secret = isSecretName(key, d.secretProperties, declared);
                  const value = draft.properties[key];
                  const stored = secret && value === SECRET_SENTINEL;

                  return (
                    <div key={key} className="flex items-start gap-2">
                      <div className="flex-1">
                        <Field
                          label={declared?.required ? `${settingLabel(key)} *` : settingLabel(key)}
                          htmlFor={`ds-prop-${key}`}
                          hint={stored ? "Stored. Type to replace it." : declared?.hint}
                        >
                          {declared?.allowedValues ? (
                            <Select
                              id={`ds-prop-${key}`}
                              value={value}
                              disabled={!canEdit}
                              onChange={(e) => setProperty(key, e.target.value)}
                              // An empty option only where empty is legal: a setting the adapter did
                              // not mark required can be left for the broker to decide.
                              options={[
                                ...(declared.required ? [] : [{ value: "", label: "—" }]),
                                ...declared.allowedValues.map((v) => ({ value: v, label: v })),
                              ]}
                            />
                          ) : secret ? (
                            <PasswordInput
                              id={`ds-prop-${key}`}
                              value={stored ? "" : value}
                              disabled={!canEdit}
                              placeholder={stored ? "••••••••" : undefined}
                              // Typing replaces the secret outright: appending to a sentinel that is
                              // not the password would save something nobody chose.
                              onChange={(e) => setProperty(key, e.target.value)}
                            />
                          ) : (
                            <TextInput
                              id={`ds-prop-${key}`}
                              type={declared?.type === "number" ? "number" : "text"}
                              value={value}
                              disabled={!canEdit}
                              onChange={(e) => setProperty(key, e.target.value)}
                            />
                          )}
                        </Field>
                      </div>
                      {canEdit && (
                        <button
                          type="button"
                          title={`Remove ${key}`}
                          onClick={() => removeProperty(key)}
                          className="mt-7 rounded-lg p-1.5 text-ink-400 hover:bg-ink-100 hover:text-danger-700"
                        >
                          <Trash2 className="size-4" />
                        </button>
                      )}
                    </div>
                  );
                })}
              </div>

              {canEdit && (
                <div className="mt-3 flex items-end gap-2">
                  <div className="w-64">
                    <Field
                      label="Add a setting"
                      htmlFor="ds-new-key"
                      hint={
                        unused.length > 0
                          ? `${provider?.label ?? "This provider"} also accepts ${unused
                              .slice(0, 3)
                              .join(", ")}${unused.length > 3 ? ` and ${unused.length - 3} more` : ""}.`
                          : "A name that looks like a credential is masked automatically."
                      }
                    >
                      <TextInput
                        id="ds-new-key"
                        list="ds-known-settings"
                        value={newKey}
                        placeholder={unused[0] ?? "QueueType"}
                        onChange={(e) => setNewKey(e.target.value)}
                        onKeyDown={(e) => e.key === "Enter" && addProperty()}
                      />
                      {/* Typing is still allowed: an adapter may read more than it declares. */}
                      <datalist id="ds-known-settings">
                        {unused.map((name) => (
                          <option key={name} value={name} />
                        ))}
                      </datalist>
                    </Field>
                  </div>
                  <Button onClick={addProperty} disabled={!newKey.trim()}>
                    <Plus className="size-4" /> Add
                  </Button>
                </div>
              )}
            </div>

          </div>
        </section>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={error ?? undefined}
          onSave={() => save.mutate(draft)}
          onDiscard={() => {
            setError(null);
            setDraft(draftOf(d));
          }}
        />
      )}
    </div>
  );
}

/** A number box whose explanation sits on a help mark beside its label. */
function CeilingInput({
  id,
  label,
  help,
  value,
  onChange,
  disabled,
  max,
  step,
}: {
  id: string;
  label: string;
  help: string;
  value: number;
  onChange: (value: number) => void;
  disabled: boolean;
  max?: number;
  step?: number;
}) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="flex items-center gap-1 text-[13px] font-medium text-ink-700">
        {label}
        <span title={help} aria-label={help} className="cursor-help text-ink-400">
          <CircleHelp className="size-3.5" />
        </span>
      </label>
      <TextInput
        id={id}
        type="number"
        min={0}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(Number(e.target.value) || 0)}
      />
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-ink-500">{label}</dt>
      <dd className="text-ink-900">{children}</dd>
    </div>
  );
}
