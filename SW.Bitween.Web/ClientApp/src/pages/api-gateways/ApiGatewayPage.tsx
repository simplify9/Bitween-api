import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pause, Pencil, Play, Plus, Search, Trash2 } from "lucide-react";
import { api, type ApiGatewayAttachment } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { finishUrlName, toUrlName, urlNameProblem } from "../../lib/identifiers";
import { HistoryCard } from "../../components/config/HistoryCard";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { Pagination } from "../../components/ui/Pagination";
import { useSubscriptionsCache, useWiredSubscriptionColumns } from "../../components/config/shared";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";

const ATTACHMENTS_PAGE_SIZE = 10;

/**
 * The same key opens the gateway from any of three places, so a partner uses whichever their
 * system can send. Basic auth splits the username off at the first colon, so it can't carry a key
 * whose name has one.
 */
const KEY_USAGE = [
  {
    label: "Header",
    tip: "Bitween's own header. Works from any system that can add a custom header.",
    value: "partnerkey: <key>",
  },
  {
    label: "Bearer token",
    tip: 'The standard Authorization header. Use it when the partner\'s tool has a "Bearer token" option.',
    value: "Authorization: Bearer <key>",
  },
  {
    label: "Basic auth",
    tip: "For tools that only ask for a username and password. The username is the key's name and the password is the key. Doesn't work for a key whose name has a colon in it.",
    value: "username: <the key's name>\npassword: <key>",
  },
];

export function ApiGatewayPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const canEdit = useSessionCan("api-gateways.edit");
  const wiredColumns = useWiredSubscriptionColumns<ApiGatewayAttachment>((a) => a.subscriptionId);
  // Where each attached subscription's response goes — not on the attachment rows themselves.
  const cachedSetups = useSubscriptionsCache().data;
  const setups = useMemo(() => cachedSetups ?? [], [cachedSetups]);
  const setupById = useMemo(() => new Map(setups.map((s) => [s.id, s])), [setups]);

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
  const [confirmingUrl, setConfirmingUrl] = useState(false);
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
  const urlProblem = urlNameProblem(urlName);

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
        <Panel title="Endpoint" description="Where partners send their documents, and how they identify themselves.">
          <div className="grid gap-4 md:grid-cols-3">
            <Field label="URL name" htmlFor="ag-url" error={urlProblem ?? undefined}>
              <TextInput
                id="ag-url"
                value={urlName}
                disabled={!canEdit}
                className="font-mono"
                onChange={(e) => setUrlName(toUrlName(e.target.value))}
              />
            </Field>
            <CopyField value={`/api/gateway/${urlName}/sync`} label="Synchronous — waits for the result" />
            <CopyField value={`/api/gateway/${urlName}/async`} label="Asynchronous — returns the exchange id" />
          </div>

          {/*
            The URLs alone are not enough to make a call, and the ways to send the key
            appear nowhere else in the product — they are only in the C# that reads them.
            Without this, handing a partner the endpoint still leaves them guessing.
          */}
          <div className="mt-4 border-t border-ink-100 pt-4">
            <p className="mb-2 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
              How a partner calls it
            </p>
            <pre className="overflow-x-auto rounded-lg bg-ink-50 px-3 py-2.5 font-mono text-[12px] leading-relaxed text-ink-700">
              {`POST /api/gateway/${urlName}/sync\n<the partner's API key, sent one of the ways below>\n\n<the document, as the body>`}
            </pre>
            <dl
              aria-label="Ways to send the key"
              className="mt-2 divide-y divide-ink-100 rounded-lg border border-ink-200"
            >
              {KEY_USAGE.map((w) => (
                <div key={w.label} className="flex items-baseline gap-3 px-3 py-2">
                  <dt title={w.tip} className="w-24 shrink-0 cursor-help text-[12px] text-ink-600">
                    {w.label}
                  </dt>
                  <dd className="min-w-0 font-mono text-[12px] break-all whitespace-pre-wrap text-ink-700">
                    {w.value}
                  </dd>
                </div>
              ))}
            </dl>
            <p className="mt-2 text-[12px] text-ink-500">
              The key is what identifies the caller — it decides which attached partner the exchange
              runs as, so each partner sends its own. Keys are issued on a{" "}
              <Link to="/partners" className="font-medium text-crimson-700 hover:underline">
                partner's page
              </Link>
              , and only shown once when created.
            </p>
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
                  // Both name columns wrap rather than shrink to fit. Every column here but
                  // "Last error" was shrink-to-content, so all the slack in a full-width panel
                  // pooled in that one column and left the other six bunched against each other
                  // down the left-hand side — which is what read as cramped, more than the
                  // padding did. `wrap` is what the width strategy says a name should use anyway.
                  wrap: true,
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
                  wrap: true,
                  cell: (a) => (
                    <Link
                      to={`/subscriptions/${a.subscriptionId}`}
                      className="text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                    >
                      {a.subscriptionName}
                    </Link>
                  ),
                },
                {
                  header: "Response",
                  headerTitle:
                    "What happens to what the delivery hands back: the response subscription it goes to, the bus message it is published as, or nothing.",
                  wrap: true,
                  cell: (a) => {
                    const s = setupById.get(a.subscriptionId);
                    if (!s) return <span className="text-ink-400">—</span>;
                    if (s.handlerId === null)
                      return (
                        <span className="text-[13px] text-ink-400" title="It delivers nothing, so there is no response.">
                          Nothing delivered
                        </span>
                      );
                    const target = setups.find((x) => x.id === s.responseSubscriptionId);
                    if (!target && !s.responseMessageTypeName)
                      return <span className="text-[13px] text-ink-400">Recorded only</span>;
                    return (
                      <span className="block space-y-0.5">
                        {s.responseSubscriptionId !== null && (
                          <Link
                            to={`/subscriptions/${s.responseSubscriptionId}`}
                            className="block text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                          >
                            {target?.name ?? `Subscription ${s.responseSubscriptionId}`}
                          </Link>
                        )}
                        {s.responseMessageTypeName && (
                          <code
                            className="block font-mono text-[11px] text-ink-500"
                            title="Published on the bus as this message"
                          >
                            {s.responseMessageTypeName}
                          </code>
                        )}
                      </span>
                    );
                  },
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

        <HistoryCard entityName="ApiGateway" entityKey={id} />
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={urlProblem ?? save.error?.message}
          onSave={() => {
            if (urlProblem) return;
            // The URL is what partners hold; changing it cuts every one of them off.
            if (finishUrlName(urlName) !== g.urlName) setConfirmingUrl(true);
            else save.mutate();
          }}
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

      {confirmingUrl && (
        <ConfirmDialog
          title="Change this gateway's URL?"
          body={
            <>
              Partners calling{" "}
              <code className="font-mono text-[12px]">/api/gateway/{g.urlName}</code> will get 404s
              until they switch to{" "}
              <code className="font-mono text-[12px]">/api/gateway/{finishUrlName(urlName)}</code>.
            </>
          }
          confirmLabel="Change URL"
          onConfirm={async () => {
            await save.mutateAsync();
          }}
          onClose={() => setConfirmingUrl(false)}
        />
      )}

      {confirmingActive && (
        <ConfirmDialog
          title={g.inactive ? `Activate ${g.name}?` : `Deactivate ${g.name}?`}
          body={
            g.inactive
              ? "Partners can call it again immediately. Nothing they sent while it was off was kept."
              : `Partners calling /api/gateway/${g.urlName} get a 503 until it is activated again. Its ${g.attachments.length} attachment${g.attachments.length === 1 ? "" : "s"} stay as they are.`
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
            navigate("/api-gateways", { replace: true });
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
