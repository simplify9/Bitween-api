import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pause, Pencil, Play, Plus, Search, Trash2 } from "lucide-react";
import { api, type ApiGatewayAttachment } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { finishUrlName, toUrlName } from "../../lib/identifiers";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { Pagination } from "../../components/ui/Pagination";
import { useWiredSubscriptionColumns } from "../../components/config/shared";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";

const ATTACHMENTS_PAGE_SIZE = 10;

export function ApiGatewayPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const canEdit = useSessionCan("api-gateways.edit");
  const wiredColumns = useWiredSubscriptionColumns<ApiGatewayAttachment>((a) => a.subscriptionId);

  const gateway = useQuery({
    queryKey: keys.apiGateways.detail(gatewayId),
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });

  const attachmentsQuery = searchParams.get("aq") ?? "";
  const attachmentsOffset = searchParams.get("aoffset") ? Number(searchParams.get("aoffset")) : 0;
  const attachments = useQuery({
    queryKey: keys.apiGateways.attachments(gatewayId, { q: attachmentsQuery, offset: attachmentsOffset }),
    queryFn: () =>
      api.searchGatewayAttachments(gatewayId, {
        search: attachmentsQuery,
        offset: attachmentsOffset,
        limit: ATTACHMENTS_PAGE_SIZE,
      }),
    placeholderData: keepPreviousData,
  });

  const setAttachmentsParam = (key: "aq" | "aoffset", value: string | null, resetOffset = key === "aq") =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        if (resetOffset) next.delete("aoffset");
        return next;
      },
      { replace: key === "aq" },
    );

  const [name, setName] = useState("");
  const [urlName, setUrlName] = useState("");
  const [removing, setRemoving] = useState<{ partnerId: number; partnerName: string } | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [confirmingActive, setConfirmingActive] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && gateway.data) {
      setName(gateway.data.name);
      setUrlName(gateway.data.urlName);
      setLoaded(true);
    }
  }, [gateway.data, loaded]);

  const dirty = useMemo(
    () => !!gateway.data && (name !== gateway.data.name || urlName !== gateway.data.urlName),
    [gateway.data, name, urlName],
  );

  const save = useMutation({
    mutationFn: () =>
      api.updateApiGateway(gatewayId, {
        name,
        urlName: finishUrlName(urlName),
        // Round-tripped, never edited here — Update replaces the record, so leaving it
        // out would reactivate a deactivated gateway on an unrelated rename.
        inactive: gateway.data?.inactive ?? false,
      }),
    onSuccess: async () => {
      // Awaited before the draft is re-synced, or the re-sync would seed from stale data.
      await queryClient.invalidateQueries({ queryKey: keys.apiGateways.all });
      setLoaded(false);
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading API gateway…" />;
  if (gateway.isError)
    return (
      <EmptyState title="This API gateway no longer exists">
        <Link to="/api-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to API gateways
        </Link>
      </EmptyState>
    );

  const g = gateway.data;

  return (
    <div className="pb-24">
      <BackLink to="/api-gateways" label="API gateways" />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex flex-wrap items-center gap-2 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle value={name} onChange={setName} disabled={!canEdit} placeholder="Gateway name" />
            {g.inactive && <Badge tone="warn">Deactivated</Badge>}
          </h1>
        </div>
        <div className="flex shrink-0 gap-2">
          {canEdit && (
            <Button
              onClick={() => setConfirmingActive(true)}
              title={
                g.inactive
                  ? "Start accepting partner calls again."
                  : "Refuse partner calls without deleting the gateway or its attachments."
              }
            >
              {g.inactive ? <Play className="size-4" /> : <Pause className="size-4" />}
              {g.inactive ? "Activate" : "Deactivate"}
            </Button>
          )}
          <Can permission="api-gateways.delete">
            <Button variant="danger" onClick={() => setDeleting(true)}>
              <Trash2 className="size-4" /> Delete
            </Button>
          </Can>
        </div>
      </div>

      {/* Endpoint above rather than beside: the attachments table below carries a
          column per configuration field and needs the full width to do it. */}
      <div className="space-y-5">
        <Panel title="Endpoint" description="Partners authenticate with their API key.">
          <div className="grid gap-4 md:grid-cols-3">
            <Field label="URL name" htmlFor="ag-url">
              <TextInput
                id="ag-url"
                value={urlName}
                disabled={!canEdit}
                className="font-mono"
                onChange={(e) => setUrlName(toUrlName(e.target.value))}
              />
            </Field>
            <CopyField value={`/api/Gateway/${urlName}/sync`} label="Synchronous — waits for the result" />
            <CopyField value={`/api/Gateway/${urlName}/async`} label="Asynchronous — returns the exchange id" />
          </div>
        </Panel>

        <Panel
          title="Partners"
          description="Each attached partner calls this gateway with its API key. Partners can share one subscription or each run their own."
          action={
            <Can permission="api-gateways.edit">
              <Button size="sm" variant="primary" onClick={() => navigate(`/api-gateways/${gatewayId}/attach`)}>
                <Plus className="size-3.5" /> Attach partner
              </Button>
            </Can>
          }
        >
          <div className="relative mb-3 max-w-xs">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
            <input
              type="search"
              value={attachmentsQuery}
              onChange={(e) => setAttachmentsParam("aq", e.target.value || null)}
              placeholder="Search attached partners"
              aria-label="Search attached partners"
              className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
            />
          </div>
          {attachments.isPending ? (
            <LoadingBlock label="Loading attached partners…" />
          ) : (
            <MiniTable
              rows={attachments.data?.result ?? []}
              rowKey={(a) => a.partnerId}
              empty={
                attachmentsQuery
                  ? "No attached partners match."
                  : "No partners attached — the gateway answers 401 to everyone. Attach a partner to bring it to life."
              }
              columns={[
                {
                  header: "Partner",
                  cell: (a) => (
                    <Link
                      to={`/partners/${a.partnerId}`}
                      className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                    >
                      {a.partnerName}
                    </Link>
                  ),
                },
                {
                  header: "Runs",
                  cell: (a) => (
                    <Link
                      to={`/subscriptions/${a.subscriptionId}`}
                      className="text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                    >
                      {a.subscriptionName}
                    </Link>
                  ),
                },
                ...wiredColumns,
                {
                  header: "",
                  align: "right",
                  cell: (a) => (
                    <Can permission="api-gateways.edit">
                      <span className="flex justify-end gap-1">
                        <button
                          onClick={() => navigate(`/api-gateways/${gatewayId}/attachments/${a.partnerId}`)}
                          aria-label={`Edit attachment for ${a.partnerName}`}
                          className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                        >
                          <Pencil className="size-3.5" />
                        </button>
                        <button
                          onClick={() => setRemoving({ partnerId: a.partnerId, partnerName: a.partnerName })}
                          aria-label={`Detach ${a.partnerName}`}
                          className="rounded-md p-1.5 text-ink-400 hover:bg-danger-50 hover:text-danger-700"
                        >
                          <Trash2 className="size-3.5" />
                        </button>
                      </span>
                    </Can>
                  ),
                },
              ]}
            />
          )}
          <div className="-mx-4 -mb-3.5 mt-1">
            <Pagination
              offset={attachmentsOffset}
              limit={ATTACHMENTS_PAGE_SIZE}
              total={attachments.data?.total ?? 0}
              onOffsetChange={(o) => setAttachmentsParam("aoffset", String(o), false)}
            />
          </div>
        </Panel>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {removing && (
        <ConfirmDialog
          title="Detach this partner?"
          body={
            <>
              <strong className="font-medium text-ink-800">{removing.partnerName}</strong> will get
              401s from this gateway immediately. The subscription itself is kept.
            </>
          }
          confirmLabel="Detach partner"
          onConfirm={async () => {
            await api.removeGatewayAttachment(gatewayId, removing.partnerId);
            void queryClient.invalidateQueries({ queryKey: keys.apiGateways.all });
            void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
          }}
          onClose={() => setRemoving(null)}
        />
      )}

      {confirmingActive && (
        <ConfirmDialog
          title={g.inactive ? `Activate ${g.name}?` : `Deactivate ${g.name}?`}
          body={
            g.inactive
              ? "Partners can call it again immediately. Nothing they sent while it was off was kept."
              : `Partners calling /api/Gateway/${g.urlName} get a 503 until it is activated again. Its ${g.attachments.length} attachment${g.attachments.length === 1 ? "" : "s"} stay as they are.`
          }
          confirmLabel={g.inactive ? "Activate" : "Deactivate"}
          onConfirm={async () => {
            await api.updateApiGateway(gatewayId, {
              name: g.name,
              urlName: g.urlName,
              inactive: !g.inactive,
            });
            await queryClient.invalidateQueries({ queryKey: keys.apiGateways.all });
          }}
          onClose={() => setConfirmingActive(false)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this API gateway?"
          body={
            <>
              <strong className="font-medium text-ink-800">{g.name}</strong> and its partner
              attachments will be gone; partners calling it start getting 404s. The subscriptions
              behind it are kept.
            </>
          }
          confirmLabel="Delete gateway"
          onConfirm={async () => {
            await api.deleteApiGateway(gatewayId);
            void queryClient.invalidateQueries({ queryKey: keys.apiGateways.all });
            void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
            navigate("/api-gateways");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
