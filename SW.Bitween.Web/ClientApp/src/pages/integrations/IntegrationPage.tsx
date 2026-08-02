import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, DownloadCloud, Pause, Play, Trash2 } from "lucide-react";
import { api, type Integration, type IntegrationDetail } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { MiniTable } from "../../components/ui/Table";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CodeBadge, EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { AdapterConfig } from "../../components/config/AdapterConfig";
import { MatchExpressionEditor } from "../../components/config/MatchExpressionEditor";
import { ScheduleEditor } from "../../components/config/ScheduleEditor";
import {
  ExchangesList,
  HealthBadge,
  TrailTable,
  TypeBadge,
  isLegacyType,
  useIntegrationsCache,
} from "../../components/config/shared";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { takePicked, useHereAsReturnTarget, withReturn } from "../../lib/returnTo";
import { formatDate, formatDateTime, formatDurationMs, timeAgo, timeUntil } from "../../lib/dates";

/**
 * What the scheduler recorded, for a job that runs on a schedule. Distinct from the
 * exchange list below it: a run can find nothing and still be a run, and a run that
 * fails never produces an exchange to explain itself.
 */
function RecentRuns({ integrationId }: { integrationId: number }) {
  const runs = useQuery({
    queryKey: ["integration-runs", integrationId],
    queryFn: () => api.listIntegrationRuns(integrationId, 20),
  });

  return (
    <Panel title="Recent runs" description="From the scheduler's own history, kept about 30 days.">
      {runs.isPending ? (
        <LoadingBlock label="Loading runs…" />
      ) : (
        <MiniTable
          rows={runs.data ?? []}
          rowKey={(r) => r.startedOn}
          empty="No runs recorded in the last 30 days."
          columns={[
            {
              header: "Started",
              cell: (r) => (
                <span title={formatDateTime(r.startedOn)} className="text-ink-800">
                  {timeAgo(r.startedOn)}
                </span>
              ),
            },
            {
              header: "Outcome",
              cell: (r) =>
                r.success === null ? (
                  <Badge tone="warn">Running</Badge>
                ) : r.success ? (
                  <Badge tone="ok">Succeeded</Badge>
                ) : (
                  <Badge tone="danger">Failed</Badge>
                ),
            },
            {
              header: "Took",
              cell: (r) => (
                <span className="text-ink-600">
                  {r.durationMs === null ? "—" : formatDurationMs(r.durationMs)}
                </span>
              ),
            },
            {
              header: "Trigger",
              cell: (r) => (
                <span className="text-ink-500">{r.manual ? "Manual" : "Schedule"}</span>
              ),
            },
            {
              header: "Error",
              truncate: true,
              cell: (r) =>
                r.error ? (
                  <span className="block truncate font-mono text-[11px] text-danger-700" title={r.error}>
                    {r.error}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
          ]}
        />
      )}
    </Panel>
  );
}

type Draft = Pick<
  Integration,
  | "name"
  | "enabled"
  | "workGroupId"
  | "retryPolicyId"
  | "receiverId"
  | "receiverProperties"
  | "validatorId"
  | "validatorProperties"
  | "mapperId"
  | "mapperProperties"
  | "handlerId"
  | "handlerProperties"
  | "matchExpression"
  | "schedules"
  | "responseIntegrationId"
  | "responseMessageTypeName"
>;

const draftOf = (d: IntegrationDetail): Draft => ({
  name: d.name,
  enabled: d.enabled,
  workGroupId: d.workGroupId,
  retryPolicyId: d.retryPolicyId,
  receiverId: d.receiverId,
  receiverProperties: structuredClone(d.receiverProperties),
  validatorId: d.validatorId,
  validatorProperties: structuredClone(d.validatorProperties),
  mapperId: d.mapperId,
  mapperProperties: structuredClone(d.mapperProperties),
  handlerId: d.handlerId,
  handlerProperties: structuredClone(d.handlerProperties),
  matchExpression: structuredClone(d.matchExpression),
  schedules: structuredClone(d.schedules),
  responseIntegrationId: d.responseIntegrationId,
  responseMessageTypeName: d.responseMessageTypeName,
});

export function IntegrationPage() {
  const { id = "" } = useParams();
  const integrationId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("subscriptions.edit");
  const canOperate = useSessionCan("subscriptions.operate");
  const canCreateWorkGroup = useSessionCan("workgroups.create");
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();

  const integration = useQuery({
    queryKey: ["integration", integrationId],
    queryFn: () => api.getIntegration(integrationId),
    retry: false,
  });
  const workGroups = useQuery({ queryKey: ["work-groups"], queryFn: () => api.listWorkGroups(), staleTime: Infinity });
  const retryPolicies = useQuery({ queryKey: ["retry-policies"], queryFn: () => api.listRetryPolicies() });
  const allIntegrations = useIntegrationsCache();
  // promoted properties power the legacy message filter
  const infoType = useQuery({
    queryKey: ["information-type", integration.data?.informationTypeId],
    queryFn: () => api.getInformationType(integration.data!.informationTypeId),
    enabled: integration.data?.type === "Internal",
  });

  const [draft, setDraft] = useState<Draft | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [confirmingPause, setConfirmingPause] = useState(false);
  const [confirmingReceive, setConfirmingReceive] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && integration.data) {
      const seeded = draftOf(integration.data);
      // returning from a "New work group" detour
      const picked = takePicked(params, "workgroup");
      if (picked !== null) {
        seeded.workGroupId = picked;
        setParams(
          (prev) => {
            const next = new URLSearchParams(prev);
            next.delete("picked");
            return next;
          },
          { replace: true },
        );
      }
      setDraft(seeded);
      setLoaded(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [integration.data, loaded]);

  const dirty = useMemo(() => {
    if (!integration.data || !draft) return false;
    return JSON.stringify(draft) !== JSON.stringify(draftOf(integration.data));
  }, [integration.data, draft]);

  const invalidate = () => {
    const detail = queryClient.invalidateQueries({ queryKey: ["integration", integrationId] });
    void queryClient.invalidateQueries({ queryKey: ["integration-rows"] });
    void queryClient.invalidateQueries({ queryKey: ["integrations"] });
    return detail;
  };

  const save = useMutation({
    mutationFn: () => api.updateIntegration(integrationId, draft!),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await invalidate();
      setLoaded(false);
    },
  });

  const pause = useMutation({
    mutationFn: () => api.pauseIntegration(integrationId),
    onSuccess: invalidate,
  });

  const receive = useMutation({
    mutationFn: () => api.receiveNow(integrationId),
    onSuccess: invalidate,
  });

  if (integration.isPending) return <LoadingBlock label="Loading integration…" />;
  if (integration.isError)
    return (
      <EmptyState title="This integration no longer exists">
        <Link to="/subscriptions" className="font-medium text-crimson-700 hover:underline">
          Back to integrations
        </Link>
      </EmptyState>
    );

  const s = integration.data;
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) =>
    setDraft((d) => (d ? { ...d, [key]: value } : d));

  const isReceiver = s.type === "Receiving";
  const hasValidator = s.type === "GatewayApiCall" || s.type === "BusGateway" || s.type === "ApiCall";
  const isInternal = s.type === "Internal";
  const isAggregation = s.type === "Aggregation";
  const paused = s.pausedOn !== null;

  // Both kinds of entry point share one table — what matters is "who can feed
  // this", not which of the two mechanisms does it.
  const entryPoints = [
    ...s.apiGatewayAttachments.map((a) => ({
      key: `ag-${a.gatewayId}-${a.partnerId}`,
      name: a.gatewayName,
      href: `/api-gateways/${a.gatewayId}`,
      kind: "API gateway",
      partnerId: a.partnerId as number | null,
      partnerName: a.partnerName as string | null,
      detail: `/${a.urlName}`,
    })),
    ...s.busGatewayRoutes.map((r, i) => ({
      key: `bg-${r.gatewayId}-${i}`,
      name: r.gatewayName,
      href: `/bus-gateways/${r.gatewayId}`,
      kind: "Bus route",
      partnerId: r.partnerId,
      partnerName: r.partnerName,
      detail: "—",
    })),
  ];

  return (
    <div className="pb-24">
      <Link
        to="/subscriptions"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Integrations
      </Link>

      <ReturnBanner />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="flex flex-wrap items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle
              value={draft?.name ?? s.name}
              onChange={(v) => set("name", v)}
              disabled={!canEdit}
              placeholder="Integration name"
            />
            <TypeBadge type={s.type} />
            {draft && (
              <button
                type="button"
                disabled={!canEdit}
                onClick={() => set("enabled", !draft.enabled)}
                title="Disabled integrations are never scheduled or matched."
                className={`shrink-0 rounded-md px-2 py-0.5 text-xs font-medium disabled:cursor-not-allowed ${
                  draft.enabled ? "bg-ok-100 text-ok-600 hover:bg-ok-200/70" : "bg-ink-100 text-ink-700 hover:bg-ink-200"
                }`}
              >
                {draft.enabled ? "Active" : "Disabled"}
              </button>
            )}
            {paused && <Badge tone="warn">Paused</Badge>}
          </h1>
          <p className="mt-1 text-sm text-ink-500">
            Carries{" "}
            <Link to={`/information-types/${s.informationTypeId}`} className="hover:underline">
              <CodeBadge code={s.informationTypeCode} name={s.informationTypeName} className="align-middle" />
            </Link>
            .
          </p>
        </div>
        <div className="flex shrink-0 gap-2">
          {canOperate && (
            <Button
              onClick={() => setConfirmingPause(true)}
              title={paused ? "Release held work and resume." : "Keep accepting work but hold it for later release."}
            >
              {paused ? <Play className="size-4" /> : <Pause className="size-4" />}
              {paused ? "Resume" : "Pause"}
            </Button>
          )}
          <Can permission="subscriptions.delete">
            <Button variant="danger" onClick={() => setDeleting(true)}>
              <Trash2 className="size-4" /> Delete
            </Button>
          </Can>
        </div>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          {isAggregation && (
            <Panel title="Aggregation">
              <p className="text-sm text-ink-500">
                Aggregation settings aren't editable in the new UI yet — they arrive in a later
                phase. Everything else on this page works as usual.
              </p>
            </Panel>
          )}

          {isReceiver && draft && (
            <>
              <Panel title="Source" description="Where documents are pulled from.">
                <AdapterConfig
                  kind="receiver"
                  adapterId={draft.receiverId}
                  properties={draft.receiverProperties}
                  onChange={(adapterId, properties) =>
                    setDraft((d) => (d ? { ...d, receiverId: adapterId, receiverProperties: properties } : d))
                  }
                  disabled={!canEdit}
                  required
                />
              </Panel>
              <Panel
                title="Schedule"
                description="When the source is checked for new documents."
                action={
                  canOperate ? (
                    <Button size="sm" onClick={() => setConfirmingReceive(true)}>
                      <DownloadCloud className="size-3.5" /> Receive now
                    </Button>
                  ) : undefined
                }
              >
                <ScheduleEditor
                  schedules={draft.schedules}
                  onChange={(schedules) => set("schedules", schedules)}
                  disabled={!canEdit}
                />
                <FormError>{receive.error?.message}</FormError>
              </Panel>
            </>
          )}

          {(isReceiver || isAggregation) && <RecentRuns integrationId={s.id} />}

          {isInternal && draft && (
            <Panel
              title="Message filter"
              description="Which documents of this type the integration picks up."
            >
              <MatchExpressionEditor
                value={draft.matchExpression}
                onChange={(matchExpression) => set("matchExpression", matchExpression)}
                properties={infoType.data?.promotedProperties ?? []}
                disabled={!canEdit}
              />
            </Panel>
          )}

          {hasValidator && draft && (
            <Panel title="Validation" description="Rejects bad documents before they enter the pipeline.">
              <AdapterConfig
                kind="validator"
                adapterId={draft.validatorId}
                properties={draft.validatorProperties}
                onChange={(adapterId, properties) =>
                  setDraft((d) => (d ? { ...d, validatorId: adapterId, validatorProperties: properties } : d))
                }
                disabled={!canEdit}
                noneLabel="None — accept every document"
              />
            </Panel>
          )}

          {draft && !isAggregation && (
            <Panel title="Transformation" description="Reshapes the document before delivery.">
              <AdapterConfig
                kind="mapper"
                adapterId={draft.mapperId}
                properties={draft.mapperProperties}
                onChange={(adapterId, properties) =>
                  setDraft((d) => (d ? { ...d, mapperId: adapterId, mapperProperties: properties } : d))
                }
                disabled={!canEdit}
                noneLabel="None — the document passes through unchanged"
                mapperEditorHref={`/subscriptions/${s.id}/mapper`}
              />
            </Panel>
          )}

          {draft && (
            <Panel title="Delivery" description="Where the document ends up.">
              <AdapterConfig
                kind="handler"
                adapterId={draft.handlerId}
                properties={draft.handlerProperties}
                onChange={(adapterId, properties) =>
                  setDraft((d) => (d ? { ...d, handlerId: adapterId, handlerProperties: properties } : d))
                }
                disabled={!canEdit}
                noneLabel="None — the document stops here"
              />
              {draft.handlerId && (
                <div className="mt-4 border-t border-ink-100 pt-4">
                  <h3 className="mb-2.5 text-[13px] font-medium text-ink-700">Response handling</h3>
                  <div className="grid gap-4 sm:grid-cols-2">
                      <Field
                        label="Feed the response into"
                        htmlFor="in-resp"
                        hint="Chains the delivery response into another integration."
                      >
                        <SearchSelect
                          id="in-resp"
                          value={draft.responseIntegrationId === null ? "" : String(draft.responseIntegrationId)}
                          disabled={!canEdit}
                          onChange={(v) => set("responseIntegrationId", v === "" ? null : Number(v))}
                          clearLabel="Nothing — responses are only recorded"
                          options={(allIntegrations.data ?? [])
                            .filter((x) => x.id !== integrationId)
                            .map((x) => ({ value: String(x.id), label: x.name }))}
                        />
                      </Field>
                      <Field
                        label="Publish the response on the bus as"
                        htmlFor="in-respbus"
                        hint="Leave empty to keep responses off the bus."
                      >
                        <TextInput
                          id="in-respbus"
                          value={draft.responseMessageTypeName ?? ""}
                          disabled={!canEdit}
                          placeholder="e.g. order-confirmation"
                          className="font-mono"
                          onChange={(e) => set("responseMessageTypeName", e.target.value || null)}
                        />
                      </Field>
                  </div>
                </div>
              )}
            </Panel>
          )}
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Status">
            <div className="space-y-2.5 text-sm">
              <div className="flex items-center justify-between">
                <span className="text-ink-500">Health</span>
                <HealthBadge isRunning={s.isRunning} consecutiveFailures={s.consecutiveFailures} />
              </div>
              {isReceiver && (
                <div className="flex items-center justify-between">
                  <span className="text-ink-500">Next run</span>
                  <span className="text-ink-800">{s.nextReceiveOn ? timeUntil(s.nextReceiveOn) : "—"}</span>
                </div>
              )}
              {paused && (
                <p className="rounded-lg bg-warn-100 px-3 py-2 text-[13px] text-warn-700">
                  Paused since {formatDate(s.pausedOn!)} — incoming work is being held and will be
                  released on resume.
                </p>
              )}
              {s.lastException && (
                <pre className="max-h-32 overflow-auto rounded-lg bg-danger-50 px-3 py-2 font-mono text-[11px] leading-relaxed whitespace-pre-wrap text-danger-800">
                  {s.lastException}
                </pre>
              )}
            </div>
          </Panel>

          {draft && (
            <Panel title="Settings">
              <div className="space-y-4">
                <Field label="Work group" htmlFor="in-wg" hint="Groups get their own queue, priority and prefetch.">
                  <SearchSelect
                    id="in-wg"
                    value={draft.workGroupId === null ? "" : String(draft.workGroupId)}
                    disabled={!canEdit}
                    onChange={(v) => set("workGroupId", v === "" ? null : Number(v))}
                    clearLabel="Ungrouped (default lane)"
                    options={(workGroups.data ?? []).map((w) => ({ value: String(w.id), label: w.name }))}
                  />
                  <div className="mt-1 flex items-center gap-3">
                    {draft.workGroupId !== null && (
                      <Link
                        to={`/work-groups/${draft.workGroupId}`}
                        className="text-[13px] font-medium text-crimson-700 hover:underline"
                      >
                        View group
                      </Link>
                    )}
                    {canCreateWorkGroup && (
                      <Link
                        to={withReturn("/work-groups/new", { to: here, label: `Assigning ${s.name} to a work group` })}
                        className="text-[13px] font-medium text-crimson-700 hover:underline"
                      >
                        + New work group
                      </Link>
                    )}
                  </div>
                </Field>
                <Field label="Retry policy" htmlFor="in-rp">
                  <SearchSelect
                    id="in-rp"
                    value={draft.retryPolicyId === null ? "" : String(draft.retryPolicyId)}
                    disabled={!canEdit}
                    onChange={(v) => set("retryPolicyId", v === "" ? null : Number(v))}
                    clearLabel="None — failures are not retried"
                    options={(retryPolicies.data ?? []).map((p) => ({ value: String(p.id), label: p.name }))}
                  />
                  {draft.retryPolicyId !== null && (
                    <Link
                      to={`/retry-policies/${draft.retryPolicyId}`}
                      className="mt-1 inline-block text-[13px] font-medium text-crimson-700 hover:underline"
                    >
                      View policy
                    </Link>
                  )}
                </Field>
              </div>
            </Panel>
          )}

          <Panel title="Connected through" description="The entry points that feed this integration.">
            <MiniTable
              rows={entryPoints}
              rowKey={(e) => e.key}
              empty={
                isReceiver
                  ? "Runs on its own schedule — no gateway needed."
                  : isLegacyType(s.type)
                    ? "Invoked directly by id (legacy)."
                    : "Not wired into any gateway yet — it never runs."
              }
              columns={[
                {
                  header: "Entry point",
                  truncate: true,
                  cell: (e) => (
                    <Link
                      to={e.href}
                      className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                    >
                      {e.name}
                    </Link>
                  ),
                },
                { header: "Kind", cell: (e) => <Badge>{e.kind}</Badge> },
                {
                  header: "Partner",
                  cell: (e) =>
                    e.partnerId !== null ? (
                      <Link
                        to={`/partners/${e.partnerId}`}
                        className="text-[13px] text-ink-600 hover:text-crimson-700 hover:underline"
                      >
                        {e.partnerName}
                      </Link>
                    ) : (
                      <span className="text-ink-400">—</span>
                    ),
                },
                {
                  header: "Path",
                  align: "right",
                  cell: (e) => <code className="font-mono text-xs text-ink-400">{e.detail}</code>,
                },
              ]}
            />
          </Panel>

          {s.watchingNotifiers.length > 0 && (
            <Panel title="Watched by" description="Notifiers alerting on this integration's outcomes.">
              <MiniTable
                rows={s.watchingNotifiers}
                rowKey={(n) => n.id}
                empty="Nothing watches this integration."
                columns={[
                  {
                    header: "Notifier",
                    truncate: true,
                    cell: (n) => (
                      <Link
                        to={`/notifiers/${n.id}`}
                        className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {n.name}
                      </Link>
                    ),
                  },
                ]}
              />
            </Panel>
          )}

          <Can permission="exchanges.view">
            <Panel title="Recent exchanges" description="Latest traffic through this integration.">
              <ExchangesList items={s.recentExchanges} />
            </Panel>
          </Can>

          {s.trail.length > 0 && (
            <Panel title="History">
              <TrailTable entries={s.trail} />
            </Panel>
          )}
        </div>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {confirmingPause && (
        <ConfirmDialog
          title={paused ? "Resume this integration?" : "Pause this integration?"}
          body={
            paused
              ? `${s.name} releases its held exchanges and starts processing again.`
              : `${s.name} keeps accepting work but holds every exchange until you resume it.`
          }
          confirmLabel={paused ? "Resume" : "Pause"}
          onConfirm={async () => {
            await pause.mutateAsync();
          }}
          onClose={() => setConfirmingPause(false)}
        />
      )}

      {confirmingReceive && (
        <ConfirmDialog
          title="Receive now?"
          body={`${s.name} checks its source immediately — anything found becomes new exchanges, outside the regular schedule.`}
          confirmLabel="Receive now"
          onConfirm={async () => {
            await receive.mutateAsync();
          }}
          onClose={() => setConfirmingReceive(false)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this integration?"
          body={
            <>
              <strong className="font-medium text-ink-800">{s.name}</strong> and its configuration
              will be gone for good. Integrations still wired into a gateway can't be deleted.
            </>
          }
          confirmLabel="Delete integration"
          onConfirm={async () => {
            await api.deleteIntegration(integrationId);
            void queryClient.invalidateQueries({ queryKey: ["integration-rows"] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
            navigate("/subscriptions");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
